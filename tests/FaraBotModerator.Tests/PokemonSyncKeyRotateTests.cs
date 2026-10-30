using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using FaraPokemonBattleAssistant.Core.Roster;
using FaraPokemonBattleAssistant.Core.Tests;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// アクセスキーの作り直し（<see cref="PokemonSyncKeyController.RotateAsync"/>）の結合。
/// 写すのと消すのは pokemon の Core（<see cref="RosterRotation"/>）の仕事で、ここでは bot が正しい順に呼ぶことを確かめる:
/// 新しいキーを保存できてから古いキーを消す・保存に失敗したら新しいキーの置き場を消す・古いキーが残ったときの案内・省いた試合数。
/// 偽サーバー（pokemon の FakeRosterServer。サイトの API と同じ動き）を、アクセスキーごとに振り分けて使う。
/// </summary>
public class PokemonSyncKeyRotateTests
{
    /// <summary>アクセスキー（Bearer）ごとに、偽サーバーへ振り分ける。知らないキーには新しい偽サーバーを作る。</summary>
    private sealed class KeyRouter : HttpMessageHandler
    {
        public Dictionary<string, FakeRosterServer> Servers { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.Authorization?.Parameter ?? "";
            if (!Servers.TryGetValue(key, out var server))
            {
                server = new FakeRosterServer { ExpectedKey = key };
                Servers[key] = server;
            }

            return server.HandleAsync(request, cancellationToken);
        }
    }

    /// <summary>古いキーの偽サーバーを 1 つ作って、振り分けに入れる。</summary>
    private static (KeyRouter Router, HttpClient Http, string OldKey) Setup()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        return (router, new HttpClient(router), oldKey);
    }

    /// <summary>サーバーに、メモが 500 文字を超える試合を 1 件置く（クライアントから送ると、送る前に 500 文字へ切られるため、サーバーの状態を直接書く）。写すときに、内容を省く対象になる。</summary>
    private static void AddLongMemoMatchOnServer(FakeRosterServer server)
    {
        server.WriteFromElsewhere(state =>
        {
            if (!state.DataSets.TryGetValue("Champions", out var data))
                state.DataSets["Champions"] = data = new DataSetRoster { ActiveTeams = new Dictionary<string, int?>() };
            data.History.Add(new RivalMatch { Id = data.NextMatchId++, Date = "2026-10-29", Memo = new string('長', 600) });
        });
    }

    [Fact]
    public async Task Rotate_saves_the_new_key_before_deleting_the_old_key()
    {
        var (router, http, oldKey) = Setup();
        using var _ = http;
        var oldServer = router.Servers[oldKey];
        var savedKeys = new List<string>();

        var outcome = await PokemonSyncKeyController.RotateAsync(PokemonDataSource.Production, oldKey, key =>
        {
            // 保存する時点では、古いキーはまだ消えていない（新しいキーを保存できてから消す）
            Assert.False(oldServer.Deleted);
            savedKeys.Add(key);
            return Task.FromResult(true);
        }, default, http);

        Assert.True(outcome.Saved);
        Assert.False(outcome.DiscardedNewKey);
        Assert.Single(savedKeys);
        Assert.NotEqual(oldKey, savedKeys[0]);
        Assert.NotNull(outcome.Finished);
        Assert.True(outcome.Finished!.OldKeyDeleted);
        Assert.True(oldServer.Deleted);
        // 消すときは、写した時点の状態と見比べる（サーバーが確かめる）
        var delete = oldServer.Requests.Last(r => r.Method == HttpMethod.Delete);
        Assert.Contains("expect=", delete.RequestUri!.Query);
        Assert.Contains("revision=", delete.RequestUri!.Query);
        // 保存されたキーの置き場は、消されていない（使われるキー）
        Assert.False(router.Servers[savedKeys[0]].Deleted);
    }

    [Fact]
    public async Task When_the_new_key_cannot_be_saved_the_new_key_is_discarded_and_the_old_key_stays()
    {
        var (router, http, oldKey) = Setup();
        using var _ = http;
        var oldServer = router.Servers[oldKey];
        string? attemptedKey = null;

        var outcome = await PokemonSyncKeyController.RotateAsync(PokemonDataSource.Production, oldKey, key =>
        {
            attemptedKey = key;
            return Task.FromResult(false);
        }, default, http);

        Assert.False(outcome.Saved);
        Assert.True(outcome.DiscardedNewKey);
        Assert.Null(outcome.Finished);
        Assert.NotNull(attemptedKey);
        Assert.True(router.Servers[attemptedKey!].Deleted);
        // 古いキーは消さない（DELETE を送らない）
        Assert.False(oldServer.Deleted);
        Assert.DoesNotContain(oldServer.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task When_the_old_key_cannot_be_deleted_the_result_says_it_remains()
    {
        var (router, http, oldKey) = Setup();
        using var _ = http;
        var oldServer = router.Servers[oldKey];
        // 消そうとするたびに、古いキーの版が進む（写し直しても追いつかない状況）
        oldServer.Override = request =>
        {
            if (request.Method == HttpMethod.Delete) oldServer.Revision++;
            return null;
        };

        var outcome = await PokemonSyncKeyController.RotateAsync(PokemonDataSource.Production, oldKey, _ => Task.FromResult(true), default, http);

        Assert.True(outcome.Saved);
        Assert.NotNull(outcome.Finished);
        Assert.False(outcome.Finished!.OldKeyDeleted);
        Assert.Equal(RosterRotationStop.RetriesExhausted, outcome.Finished.Stop);
        Assert.False(oldServer.Deleted);
        // 上限は MaxRetries 回。それ以上は消そうとしない
        Assert.Equal(RosterRotation.MaxRetries, oldServer.Requests.Count(r => r.Method == HttpMethod.Delete));

        var message = PokemonSyncKeyController.RotateResultMessage(outcome.Finished);
        Assert.Contains("古いアクセスキーは残っています", message);
        Assert.Contains("古いアクセスキーは使わないでください", message);
        Assert.DoesNotContain(oldKey, message);
    }

    [Fact]
    public async Task The_trimmed_match_count_is_reported_once_and_is_not_doubled()
    {
        var (router, http, oldKey) = Setup();
        using var _ = http;
        // 写す前に、内容を省く試合を 1 件入れておく（前半で 1 件省く。Core の結果は前半の分を含むので、足し直さない）
        AddLongMemoMatchOnServer(router.Servers[oldKey]);

        var outcome = await PokemonSyncKeyController.RotateAsync(PokemonDataSource.Production, oldKey, _ => Task.FromResult(true), default, http);

        Assert.True(outcome.Finished!.OldKeyDeleted);
        Assert.Equal(1, outcome.Finished.TrimmedMatches);
        Assert.Contains("内容を省いて写した試合が 1 件", PokemonSyncKeyController.RotateResultMessage(outcome.Finished));
    }

    [Fact]
    public void The_message_without_trimmed_matches_has_no_trimmed_note()
    {
        var message = PokemonSyncKeyController.RotateResultMessage(new RosterRotationResult(true, 0, null));

        Assert.Contains("古いアクセスキーは使えなくなりました", message);
        Assert.DoesNotContain("省いて写した", message);
    }
}
