using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonBattleAssistant.Core.Roster;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 登録データ（ポケモン・チーム）をサイトと共有するアクセスキーの発行・入力の確認・作り直しを行います。
/// 作り直しの中身は pokemon の Core の <see cref="RosterRotation"/>（サイトと同じ処理）を使います。
/// アクセスキーはパスワードと同じ扱いで、ログ・チャット・画面のメッセージには出しません。
/// </summary>
public static class PokemonSyncKeyController
{
    /// <summary>
    /// サイトの「ポケモン登録」ページ（/my-pokemon）の URL を返します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    public static string RosterPageUrl(PokemonDataSource dataSource)
    {
        return PokemonCommandController.SiteBaseUrl(dataSource) + "my-pokemon";
    }

    /// <summary>
    /// 新しいアクセスキーを作り、クラウドにつながるか確かめます（新しいキーの置き場は空です）。
    /// </summary>
    /// <param name="dataSource">データの取得元（同じサイトのクラウドを使う）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>新しいアクセスキー</returns>
    /// <exception cref="RosterUnavailableException">クラウドに届かない</exception>
    public static async Task<string> IssueAsync(PokemonDataSource dataSource, CancellationToken cancellationToken = default)
    {
        var key = RemoteRosterStore.GenerateKey();
        await CreateStore(dataSource, key).LoadAsync(cancellationToken);
        return key;
    }

    /// <summary>
    /// アクセスキーでクラウドの登録データを読みます（キーが使えるかの確認を兼ねる）。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="syncKey">アクセスキー（<see cref="RemoteRosterStore.IsValidKey"/> を満たすもの）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>登録データ（クラウドが空なら空の登録データ）</returns>
    /// <exception cref="RosterUnavailableException">キーが正しくない・作り直された・クラウドに届かない</exception>
    public static async Task<RosterState> LoadAsync(PokemonDataSource dataSource, string syncKey,
        CancellationToken cancellationToken = default)
    {
        return RosterRepository.Deserialize(await CreateStore(dataSource, syncKey).LoadAsync(cancellationToken));
    }

    /// <summary>
    /// アクセスキーの作り直しの結果。
    /// </summary>
    /// <param name="Saved">新しいキーを保存できたか（false のときは、古いキーのまま）</param>
    /// <param name="DiscardedNewKey">保存できなかった新しいキーの置き場を消せたか（<see cref="Saved"/> が false のときだけ意味がある）</param>
    /// <param name="Finished">古いキーを消す後半の結果（保存できたときだけ）</param>
    public sealed record RotateOutcome(bool Saved, bool DiscardedNewKey, RosterRotationResult? Finished);

    /// <summary>
    /// アクセスキーを作り直します。登録データ・相手チーム・試合の履歴を新しいキーへ写し、
    /// <paramref name="saveNewKey"/> で新しいキーを保存できてから、古いキーを消します。
    /// 保存できなかったときは、新しいキーの置き場を消します（古いキーのまま）。
    /// 新しいキーは、この処理の外（画面・ログ）には出しません。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="oldKey">今のアクセスキー</param>
    /// <param name="saveNewKey">新しいキーを設定ファイルへ保存する処理（保存できたら true）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="http">通信に使う HttpClient（省略時は既定のもの。テスト用）</param>
    /// <exception cref="RosterUnavailableException">写せなかった（古いキーのまま）</exception>
    public static async Task<RotateOutcome> RotateAsync(PokemonDataSource dataSource, string oldKey, Func<string, Task<bool>> saveNewKey,
        CancellationToken cancellationToken = default, HttpClient? http = null)
    {
        var client = http ?? PokemonCommandController.HttpClient;
        var baseUrl = PokemonCommandController.SiteBaseUrl(dataSource);

        // 1. 新しいキーの置き場へ写す（古いキーはまだ消さない。写せなかったら、Core が新しいキーの置き場を消して例外にする）
        var plan = await RosterRotation.PrepareAsync(client, baseUrl, oldKey, cancellationToken);

        // 2. 新しいキーを保存できてから、古いキーを消す。保存できなければ、新しいキーの置き場を消して今のキーのまま
        if (!await saveNewKey(plan.NewKey))
        {
            var discarded = await RosterRotation.DiscardAsync(client, baseUrl, plan.NewKey, CancellationToken.None);
            return new RotateOutcome(false, discarded, null);
        }

        // 3. 古いキーを消す。ここで例外が出ても、新しいキーへの切り替えは済んでいるので、消せなかった扱いにする
        RosterRotationResult finished;
        try
        {
            finished = await RosterRotation.FinishAsync(client, baseUrl, plan, cancellationToken);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> 古いアクセスキーの削除に失敗しました: {ex.GetType().Name}");
            finished = new RosterRotationResult(false, plan.TrimmedMatches, RosterRotationStop.Failed);
        }

        return new RotateOutcome(true, false, finished);
    }

    /// <summary>
    /// 作り直しの後の案内文（古いキーを消せたか・内容を省いた試合の数）を返します。キーは含みません。
    /// </summary>
    /// <param name="finished">古いキーを消す後半の結果（<see cref="RosterRotationResult.TrimmedMatches"/> は前半の分も含む）</param>
    public static string RotateResultMessage(RosterRotationResult finished)
    {
        var trimmed = finished.TrimmedMatches;
        return (finished.OldKeyDeleted
                ? "アクセスキーを作り直しました。古いアクセスキーは使えなくなりました。"
                : "アクセスキーを作り直しました。ただし、古いアクセスキーは残っています。写している間に古いアクセスキーへ書かれた内容は、新しいアクセスキーに入っていないかもしれません。古いアクセスキーは使わないでください。") +
            "\n\nサイトの「データ共有」ページで、新しいアクセスキーを入力し直してください（「コピー」でコピーできます）。" +
            (trimmed > 0
                ? $"\n\n内容を省いて写した試合が {trimmed} 件あります（サーバーの上限のため。その試合の内容は、新しいアクセスキーに入っていません）。"
                : "");
    }

    /// <summary>
    /// 使わなくなったアクセスキーの置き場を、best-effort で消します（新しいキーを発行して保存できなかったときなど）。
    /// 消せなくても例外にはせず false を返す（呼び出し側は、キーを使わないことだけを案内する）。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="syncKey">消す置き場のアクセスキー（今使っているキーを渡さないこと）</param>
    /// <returns>消せたか</returns>
    public static Task<bool> DiscardKeyAsync(PokemonDataSource dataSource, string syncKey)
    {
        return RosterRotation.DiscardAsync(PokemonCommandController.HttpClient, PokemonCommandController.SiteBaseUrl(dataSource), syncKey);
    }

    /// <summary>
    /// 登録データの全ゲーム合計の登録数とチーム数を返します。
    /// </summary>
    /// <param name="state">登録データ</param>
    public static (int Pokemon, int Teams) Count(RosterState state)
    {
        return (state.DataSets.Values.Sum(d => d.Pokemon.Count), state.DataSets.Values.Sum(d => d.Teams.Count));
    }

    private static RemoteRosterStore CreateStore(PokemonDataSource dataSource, string syncKey)
    {
        return new RemoteRosterStore(PokemonCommandController.HttpClient, PokemonCommandController.SiteBaseUrl(dataSource), syncKey);
    }
}
