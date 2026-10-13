using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonBattleAssistant.Core.Roster;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 登録データ（ポケモン・チーム）をサイトと共有するアクセスキーの発行・入力の確認・作り直しを行います。
/// アクセスキーはパスワードと同じ扱いで、ログ・チャット・画面のメッセージには出しません。
/// </summary>
public static class PokemonSyncKeyController
{
    /// <summary>
    /// サイトの「ポケモン登録」ページの URL を返します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    public static string RosterPageUrl(PokemonDataSource dataSource)
    {
        return PokemonCommandController.SiteBaseUrl(dataSource) + "roster";
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
    /// アクセスキーを作り直します。今のクラウドのデータを新しいキーの置き場に移し、古いキーの置き場を消します
    /// （古いキーは使えなくなるので、サイトでも新しいキーを入力し直す必要があります）。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="oldKey">今のアクセスキー</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>NewKey: 新しいアクセスキー。OldKeyDeleted: 古いキーの置き場を消せたか（false でも新しいキーへの移動は済んでいる）</returns>
    /// <exception cref="RosterUnavailableException">データを移せなかった（このときは古いキーのまま）</exception>
    public static async Task<(string NewKey, bool OldKeyDeleted)> RotateAsync(PokemonDataSource dataSource, string oldKey,
        CancellationToken cancellationToken = default)
    {
        var old = CreateStore(dataSource, oldKey);
        var key = RemoteRosterStore.GenerateKey();
        var remote = CreateStore(dataSource, key);
        var json = await old.LoadAsync(cancellationToken);
        if (json is not null) await remote.SaveAsync(json, cancellationToken); // 新しいキーの置き場は空（版 0）
        else await remote.LoadAsync(cancellationToken); // つながるか確かめる
        try
        {
            await old.DeleteAsync(cancellationToken);
            return (key, true);
        }
        catch (RosterUnavailableException)
        {
            return (key, false);
        }
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
        return new RemoteRosterStore(PokemonCommandController.HttpClient, PokemonCommandController.SiteBaseUrl(dataSource),
            syncKey);
    }
}
