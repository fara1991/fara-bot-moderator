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

        Assert.True(finished);
        var delete = router.Servers[oldKey].Requests.Last(r => r.Method == HttpMethod.Delete);
        // 履歴の見比べ（expect=）つきで消す。写した時点の状態のときだけ消える（サーバー側の確認）
        Assert.Contains("expect=", delete.RequestUri!.Query);
        Assert.True(router.Servers[oldKey].Deleted);
    }
}
