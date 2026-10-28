using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using FaraPokemonBattleAssistant.Core.Roster;
using FaraPokemonBattleAssistant.Core.Tests;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// アクセスキーの作り直し（<see cref="PokemonSyncKeyController"/>）の不変条件。
/// 古いキーは、新しいキーへ写し終えて、新しいキーを保存したあとの <c>FinishRotateAsync</c> でだけ消す。
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

    [Fact]
    public async Task Prepare_rotate_does_not_delete_the_old_key()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);

        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        Assert.Equal(oldKey, pending.OldKey);
        Assert.NotEqual(oldKey, pending.NewKey);
        // 新しいキーへ写すまでは、古いキーの置き場に触らない（DELETE を送らない）
        Assert.DoesNotContain(router.Servers[oldKey].Requests, r => r.Method == HttpMethod.Delete);
        Assert.False(router.Servers[oldKey].Deleted);
    }

    [Fact]
    public async Task Finish_rotate_deletes_the_old_key_only_with_the_copied_history_state()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);
        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        var finished = await PokemonSyncKeyController.FinishRotateAsync(PokemonDataSource.Production, pending,
            default, http);

        Assert.True(finished.OldKeyDeleted);
        var delete = router.Servers[oldKey].Requests.Last(r => r.Method == HttpMethod.Delete);
        // 履歴の見比べ（expect=）と、登録データの版（revision=）つきで消す。写した時点の状態のときだけ消える（サーバー側の確認）
        Assert.Contains("expect=", delete.RequestUri!.Query);
        Assert.Contains("revision=", delete.RequestUri!.Query);
        Assert.True(router.Servers[oldKey].Deleted);
    }

    [Fact]
    public async Task Finish_rotate_copies_a_roster_change_made_on_the_old_key_during_the_rotation()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);
        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        // 写している間に、別の端末・BOT が古いキーへ登録を書く（登録データの版が変わる）
        var writer = new RemoteRosterStore(http, RemoteRosterStore.ProductionBaseUrl, oldKey);
        await new RosterRepository(writer).ChangeRivalsAsync("Champions", roster =>
        {
            roster.Rivals.Add(new RivalPokemon { Id = roster.NextRivalId++, PokemonId = 445, PokemonName = "写している間の相手" });
            return (0, true);
        });

        var finished = await PokemonSyncKeyController.FinishRotateAsync(PokemonDataSource.Production, pending,
            default, http);

        Assert.True(finished.OldKeyDeleted);
        Assert.True(router.Servers[oldKey].Deleted);
        // 新しいキーには、写している間に足された登録も入っている（消さずに、写し直してから古いキーを消した）
        var copied = RosterRepository.Deserialize(router.Servers[pending.NewKey].RosterJson!);
        Assert.Contains(copied.DataSets["Champions"].Rivals, r => r.PokemonName == "写している間の相手");
    }

    [Fact]
    public async Task Finish_rotate_keeps_the_old_key_after_five_failed_attempts()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        var old = new FakeRosterServer { ExpectedKey = oldKey };
        router.Servers[oldKey] = old;
        // 消そうとするたびに、古いキーの版が進む（写し直しても追いつかない状況）
        old.Override = request =>
        {
            if (request.Method == HttpMethod.Delete) old.Revision++;
            return null;
        };
        using var http = new HttpClient(router);
        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        var finished = await PokemonSyncKeyController.FinishRotateAsync(PokemonDataSource.Production, pending,
            default, http);

        Assert.False(finished.OldKeyDeleted);
        Assert.False(old.Deleted);
        // 上限は 5 回。それ以上は消そうとしない
        Assert.Equal(5, old.Requests.Count(r => r.Method == HttpMethod.Delete));
    }

    /// <summary>
    /// サーバーに、メモが 500 文字を超える試合を 1 件置く（クライアントから送ると、送る前に 500 文字へ切られるため、サーバーの状態を直接書く）。
    /// 写すときに、内容を省く対象になる。
    /// </summary>
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
    public async Task Prepare_rotate_counts_the_matches_copied_without_their_content()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);
        // 写す前に、内容を省く試合を 1 件入れておく
        AddLongMemoMatchOnServer(router.Servers[oldKey]);

        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        Assert.Equal(1, pending.TrimmedMatches);
    }

    [Fact]
    public async Task Finish_rotate_counts_the_matches_added_during_the_rotation_that_were_trimmed()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);
        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        // 写している間に、古いキーへ内容を省く試合が足される（後半で写し、件数に入る）
        AddLongMemoMatchOnServer(router.Servers[oldKey]);

        var finished = await PokemonSyncKeyController.FinishRotateAsync(PokemonDataSource.Production, pending,
            default, http);

        Assert.True(finished.OldKeyDeleted);
        Assert.Equal(1, finished.TrimmedMatches);
    }

    [Fact]
    public async Task Discard_removes_only_the_new_key_and_never_touches_the_old_key()
    {
        var router = new KeyRouter();
        var oldKey = RemoteRosterStore.GenerateKey();
        router.Servers[oldKey] = new FakeRosterServer { ExpectedKey = oldKey };
        using var http = new HttpClient(router);
        var pending = await PokemonSyncKeyController.PrepareRotateAsync(PokemonDataSource.Production, oldKey,
            default, http);

        var discarded = await PokemonSyncKeyController.DiscardKeyAsync(PokemonDataSource.Production, pending.NewKey, http);

        Assert.True(discarded);
        Assert.True(router.Servers[pending.NewKey].Deleted);
        Assert.False(router.Servers[oldKey].Deleted);
        Assert.DoesNotContain(router.Servers[oldKey].Requests, r => r.Method == HttpMethod.Delete);
    }

    /// <summary>どの要求にも、つながらない（接続に失敗した）ように返す。</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unreachable (test)");
    }

    [Fact]
    public async Task Discard_returns_false_instead_of_throwing_when_the_server_cannot_be_reached()
    {
        using var http = new HttpClient(new UnreachableHandler());

        var discarded = await PokemonSyncKeyController.DiscardKeyAsync(PokemonDataSource.Production,
            RemoteRosterStore.GenerateKey(), http);

        Assert.False(discarded);
    }
}
