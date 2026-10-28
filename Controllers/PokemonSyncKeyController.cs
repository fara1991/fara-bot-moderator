using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    /// 古いキーを消す（写した履歴の状態と見比べる）のを試す回数。履歴の写しの途中で足された試合は、そのたびに新しいキーへ写す。
    /// </summary>
    private const int MaxRotateRetries = 5;

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
    /// アクセスキーの作り直しの前半の結果。登録データと履歴は新しいキーへ写し終えているが、古いキーはまだ消していない。
    /// </summary>
    /// <param name="OldKey">今のアクセスキー（まだ使える）</param>
    /// <param name="NewKey">新しいアクセスキー（まだ保存していない）</param>
    /// <param name="CopiedTotals">写した時点の、古いキーの履歴の状態（古いキーを消すときの見比べに使う）</param>
    /// <param name="CopiedRevision">写した登録データの版。古いキーへ後から登録が書かれていたら、消さずに写し直す</param>
    /// <param name="RosterKeys">写した登録データのゲーム（写し直すときに、古いキーで空になったゲームも空として書くため）</param>
    /// <param name="TrimmedMatches">サーバーの上限に収めるため、内容を省いて写した試合の数</param>
    public sealed record PendingRotation(string OldKey, string NewKey,
        IReadOnlyDictionary<string, HistoryTotal> CopiedTotals, long CopiedRevision,
        IReadOnlyList<string> RosterKeys, int TrimmedMatches);

    /// <summary>
    /// アクセスキーの作り直し（前半）。今のクラウドの登録データ・相手チーム・試合の履歴を新しいキーの置き場に写す。
    /// 古いキーは消さない（新しいキーを保存できてから <see cref="FinishRotateAsync"/> で消す）。
    /// 写せなかったときは、新しいキーの置き場を消して例外にする（古いキーのまま）。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="oldKey">今のアクセスキー</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="http">通信に使う HttpClient（省略時は既定のもの。テスト用）</param>
    /// <exception cref="RosterUnavailableException">データを移せなかった（このときは古いキーのまま）</exception>
    public static async Task<PendingRotation> PrepareRotateAsync(PokemonDataSource dataSource, string oldKey,
        CancellationToken cancellationToken = default, HttpClient? http = null)
    {
        var old = CreateStore(dataSource, oldKey, http);
        var json = await old.LoadAsync(cancellationToken);
        // 相手チームを読めていないと、新しいキーに相手チームと履歴が移らないまま、古いキーを消してしまう
        if (old.RivalUnreadable)
            throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                "クラウドの相手チームを読み込めなかったため、キーを作り直せません。時間をおいて、もう一度試してください");
        // 写した登録データの版。古いキーを消すとき、この版のままかを確かめる（読んでから書かれていたら、読み直して写し直す）
        var copiedRevision = RevisionOf(old);
        var rosterKeys = json is null ? new List<string>() : RosterRepository.Deserialize(json).DataSets.Keys.ToList();

        var key = RemoteRosterStore.GenerateKey();
        var remote = CreateStore(dataSource, key, http);
        try
        {
            // 新しいキーは、写し終わるまで誰も使わない。途中で失敗したら、新しいキーは使わずに置き場を消す
            if (json is not null) await remote.SaveAsync(json, cancellationToken);
            else await remote.LoadAsync(cancellationToken);
            var (trimmed, totals) = await CopyHistoryAsync(old, remote, cancellationToken);
            return new PendingRotation(oldKey, key, totals, copiedRevision, rosterKeys, trimmed);
        }
        catch
        {
            try
            {
                await remote.DeleteAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // 消せなくても新しいキーは使わない
            }

            throw;
        }
    }

    /// <summary>
    /// アクセスキーの作り直し（後半）。新しいキーを保存したあとに呼ぶ。古いキーを使えなくし、置き場を消す。
    /// 写している間に別の端末・BOT が履歴を足していたら、消さずに、足された分だけ新しいキーへ写して、もう一度試す。
    /// </summary>
    /// <returns>古いキーを消せたか（false のときは、古いキーは残っている。写している間に古いキーへ書かれた内容は、新しいキーに入っていないかもしれない）</returns>
    public static async Task<bool> FinishRotateAsync(PokemonDataSource dataSource, PendingRotation pending,
        CancellationToken cancellationToken = default, HttpClient? http = null)
    {
        var old = CreateStore(dataSource, pending.OldKey, http);
        var remote = CreateStore(dataSource, pending.NewKey, http);
        var expectedHistory = pending.CopiedTotals;
        long? expectedRevision = pending.CopiedRevision;
        var rosterKeys = pending.RosterKeys.ToList();
        for (var attempt = 0; attempt < MaxRotateRetries; attempt++)
        {
            try
            {
                // 確認と削除はサーバーで 1 つの処理なので、その間に取りこぼしは起きない（履歴の状態と登録データの版の両方を見る）
                await old.DeleteIfUnchangedAsync(expectedHistory, expectedRevision, cancellationToken);
                return true;
            }
            catch (HistoryChangedException changed)
            {
                try
                {
                    // 登録データの版が違う（または版が分からない）ときは、古いキーの登録データを読み直して、新しいキーへ全部写し直す
                    if (changed.CurrentRevision != expectedRevision)
                    {
                        var reread = await old.LoadAsync(cancellationToken);
                        // 相手チームを読めないまま写すと、新しいキーの相手チームと履歴を空にしてしまう
                        if (old.RivalUnreadable)
                            throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                                "クラウドの相手チームを読み込めなかったため、写し直せません。時間をおいて、もう一度試してください");
                        var state = reread is null ? new RosterState() : RosterRepository.Deserialize(reread);
                        rosterKeys = rosterKeys.Union(state.DataSets.Keys).ToList();
                        await RewriteRosterAsync(remote, state, rosterKeys, cancellationToken);
                        expectedRevision = RevisionOf(old);
                    }

                    await CopyNewMatchesAsync(old, remote, expectedHistory, changed.Current, cancellationToken);
                    expectedHistory = changed.Current;
                }
                catch (Exception ex) when (ex is RosterUnavailableException or HistoryLimitException or RosterConflictException)
                {
                    break;
                }
            }
            catch (RosterUnavailableException)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// 古いキーの登録データを、新しいキーへ全部書き直す（ポケモン → チーム → 相手チームの順。ゲームは <paramref name="keys"/> の全部）。
    /// 写したあとに古いキーへ書かれた分を反映する。古いキーで空になったゲームは空として書く。
    /// </summary>
    private static async Task RewriteRosterAsync(RemoteRosterStore remote, RosterState state, IReadOnlyList<string> keys, CancellationToken ct)
    {
        // 書く前に新しいキーの版を読む（版 0 のまま書くと、409 になる）
        await remote.LoadAsync(ct);
        foreach (var part in new[] { RosterPart.Pokemon, RosterPart.Teams, RosterPart.Rival })
            foreach (var key in keys)
                await remote.SavePartAsync(state, key, part, ct);
        VerifyCounts(state, await remote.LoadAsync(ct));
    }

    /// <summary>
    /// 写した結果を確かめる（ゲームごとのポケモン・チーム・相手チームの数が同じか）。違えば例外（写し直しを中止する）。
    /// </summary>
    private static void VerifyCounts(RosterState source, string? copiedJson)
    {
        var copied = copiedJson is null ? new RosterState() : RosterRepository.Deserialize(copiedJson);
        foreach (var (key, roster) in source.DataSets)
        {
            var got = copied.DataSets.TryGetValue(key, out var c) ? c : new DataSetRoster();
            if (roster.Pokemon.Count != got.Pokemon.Count || roster.Teams.Count != got.Teams.Count || roster.Rivals.Count != got.Rivals.Count)
                throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                    "クラウドに写せない内容があったため、写し直しを中止しました。古いアクセスキーは消していません");
        }
    }

    /// <summary>直近の読み込み（または保存）で分かった登録データの版。</summary>
    private static long RevisionOf(RemoteRosterStore remote) =>
        remote.Revision ?? throw new InvalidOperationException("登録データの版を読めていません");

    /// <summary>
    /// 登録データの全ゲーム合計の登録数とチーム数を返します。
    /// </summary>
    /// <param name="state">登録データ</param>
    public static (int Pokemon, int Teams) Count(RosterState state)
    {
        return (state.DataSets.Values.Sum(d => d.Pokemon.Count), state.DataSets.Values.Sum(d => d.Teams.Count));
    }

    /// <summary>
    /// 古いキーの履歴を、全部のゲーム分、新しいキーへ写す。新しいキーに写した件数がそろっていることを確かめる。
    /// （サイトの CloudSync.CopyHistoryAsync と同じ手順）
    /// </summary>
    private static async Task<(int Trimmed, IReadOnlyDictionary<string, HistoryTotal> Totals)> CopyHistoryAsync(
        RemoteRosterStore source, RemoteRosterStore target, CancellationToken ct)
    {
        var before = await source.DataSetTotalsAsync(ct);
        var trimmed = 0;
        foreach (var (dataSetKey, state) in before)
        {
            var matches = await ReadAllHistoryOldestFirstAsync(source, dataSetKey, state.Total, state.NextId, ct);
            trimmed += await target.AppendManyAsync(dataSetKey, matches, ct);
        }

        var copied = await target.DataSetTotalsAsync(ct);
        if (copied.Count != before.Count || before.Any(x => !copied.TryGetValue(x.Key, out var other) || other.Total != x.Value.Total))
            throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                "履歴を新しいアクセスキーへ写せなかったため、中止しました。もう一度試してください");
        return (trimmed, before);
    }

    /// <summary>
    /// 写し終えたあとに、古いキーへ足された試合（写した時点の次の Id 以降）だけを、新しいキーへ足す。
    /// </summary>
    private static async Task CopyNewMatchesAsync(RemoteRosterStore source, RemoteRosterStore target,
        IReadOnlyDictionary<string, HistoryTotal> previous, IReadOnlyDictionary<string, HistoryTotal> current, CancellationToken ct)
    {
        foreach (var (dataSetKey, state) in current)
        {
            var from = previous.TryGetValue(dataSetKey, out var was) ? was.NextId : 1;
            if (state.NextId <= from) continue; // 足されていない（消されただけ）
            var added = new List<RivalMatch>();
            int? before = state.NextId; // 今回の状態の次の Id より前だけ（それ以降は、次の回で写す。同じ試合を 2 度写さない）
            var finished = false;
            for (var pages = 0; pages <= (state.NextId - from) / 200 + 2 && !finished; pages++)
            {
                var page = await source.ListAsync(dataSetKey, 200, before, ct);
                added.AddRange(page.Items.TakeWhile(m => m.Id >= from));
                finished = !page.HasMore || page.Items.Count == 0 || page.Items[^1].Id < from;
                if (!finished) before = page.Items[^1].Id;
            }

            if (!finished) // 読み切れていないまま写すと、残りを失うので、中止する
                throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                    "履歴を最後まで読めなかったため、キーを作り直せません。もう一度試してください");
            added.Reverse();
            if (added.Count > 0) await target.AppendManyAsync(dataSetKey, added, ct);
        }
    }

    /// <summary>
    /// 1 つのゲームの履歴を、古い順に全部読む（件数が合わなければ例外）。
    /// </summary>
    private static async Task<List<RivalMatch>> ReadAllHistoryOldestFirstAsync(RemoteRosterStore source, string dataSetKey,
        int expectedTotal, int nextId, CancellationToken ct)
    {
        var all = new List<RivalMatch>();
        int? before = nextId; // 一覧を取った時点の次の Id より前だけ読む（読んでいる間に足された試合は、あとで足された分として写す）
        for (var pages = 0; pages <= expectedTotal / 200 + 2; pages++)
        {
            var page = await source.ListAsync(dataSetKey, 200, before, ct);
            // 次のページの頼み方（before）は、必ず前より小さくなる（ListAsync が確かめている）。空なのに続きがあるのはおかしい
            if (page.Items.Count == 0 && page.HasMore) break;
            all.AddRange(page.Items);
            if (!page.HasMore || page.Items.Count == 0)
            {
                all.Reverse();
                return all.Count == expectedTotal
                    ? all
                    : throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
                        "履歴を最後まで読めなかったため、キーを作り直せません。もう一度試してください");
            }

            before = page.Items[^1].Id;
        }

        throw new RosterUnavailableException(RosterUnavailableReason.InvalidResponse, null,
            "履歴を最後まで読めなかったため、キーを作り直せません。もう一度試してください");
    }

    private static RemoteRosterStore CreateStore(PokemonDataSource dataSource, string syncKey, HttpClient? http = null)
    {
        return new RemoteRosterStore(http ?? PokemonCommandController.HttpClient, PokemonCommandController.SiteBaseUrl(dataSource),
            syncKey);
    }
}
