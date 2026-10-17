using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonBattleAssistant.Core.Data;
using FaraPokemonBattleAssistant.Core.Models;
using FaraPokemonBattleAssistant.Core.Roster;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 登録したポケモン・チーム（クラウドの登録データ）を画面に表示するために読み込みます。読むだけで、書き換えはしません。
/// </summary>
public static class PokemonRosterViewer
{
    /// <summary>
    /// 登録データをアクセスキーでクラウドから読み込み、ゲームごとにまとめます。特性・持ち物の名前は取得元のデータで引き、
    /// データを取得できないときは ID のまま表示します。
    /// </summary>
    /// <param name="dataSource">登録データのクラウドと、名前を引くデータの取得元</param>
    /// <param name="syncKey">アクセスキー</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>登録のあるゲームの一覧（登録が無ければ空）</returns>
    /// <exception cref="RosterUnavailableException">アクセスキーが無い・クラウドから読めない</exception>
    public static async Task<IReadOnlyList<RosterGameView>> LoadAsync(PokemonDataSource dataSource, string syncKey,
        CancellationToken cancellationToken = default)
    {
        var state = await new RosterRepository(PokemonCommandController.CreateRosterStore(dataSource, syncKey))
            .GetAsync(cancellationToken);
        var catalog = PokemonCommandController.CreateCatalog(dataSource);

        IReadOnlyList<DataSetInfo> dataSets;
        try
        {
            dataSets = await catalog.GetDataSetsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dataSets = Array.Empty<DataSetInfo>();
        }

        var games = new List<RosterGameView>();
        foreach (var (key, roster) in state.DataSets)
        {
            if (roster.Pokemon.Count == 0 && roster.Teams.Count == 0) continue;
            var info = dataSets.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            PokemonDataSet? data = null;
            if (info is not null)
            {
                try
                {
                    data = await catalog.GetDataSetAsync(info.Key, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    data = null;
                }
            }

            games.Add(new RosterGameView(key, info?.Name ?? key, info?.CommandPrefix ?? "",
                roster.Pokemon.Select(p => ToRow(p, data)).ToList(),
                roster.Teams.OrderBy(t => t.Number).Select(t => ToRow(t, roster, data)).ToList(),
                data is not null));
        }

        // 取得元のデータの並び（チャンピオンズ → SV → 剣盾 …）に合わせる。データに無いゲームは最後
        var order = dataSets.Select((d, i) => (d.Key, i))
            .ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
        return games.OrderBy(g => order.GetValueOrDefault(g.Key, int.MaxValue)).ToList();
    }

    private static RosterPokemonRow ToRow(RegisteredPokemon p, PokemonDataSet? data)
    {
        var ability = p.AbilityId is { } abilityId ? data?.FindAbility(abilityId)?.Name ?? $"ID {abilityId}" : "（指定なし）";
        var tera = string.IsNullOrEmpty(p.TeraType) ? "" : TypeNames.ToJapanese(p.TeraType);
        // 「A32 D2 S32」のように区切る（0 の能力は書かない）
        var evs = string.Join(" ", Enum.GetValues<Stat>().Where(s => p.EVs[s] != 0)
            .Select(s => $"{StatNames.Letter(s)}{p.EVs[s]}"));
        return new RosterPokemonRow($"#{p.Id}", p.PokemonName, p.Nature, evs.Length == 0 ? "無振り" : evs, ability, tera);
    }

    private static RosterTeamRow ToRow(Team team, DataSetRoster roster, PokemonDataSet? data)
    {
        var members = team.Slots.Select(slot =>
        {
            var entry = roster.Pokemon.FirstOrDefault(p => p.Id == slot.EntryId);
            var name = entry is null ? "（削除済み）" : entry.PokemonName;
            var item = slot.ItemId is { } itemId ? " @" + (data?.FindItem(itemId)?.Name ?? $"ID {itemId}") : "";
            return $"#{slot.EntryId} {name}{item}";
        });
        // 1 行に 1 匹ずつ並べる
        return new RosterTeamRow($"チーム{team.Number}", roster.ActiveTeam == team.Number ? "使用中" : "",
            string.Join(Environment.NewLine, members));
    }
}

/// <summary>
/// 1 つのゲームの登録データ
/// </summary>
/// <param name="Key">データセットのキー（例: Champions）</param>
/// <param name="Name">ゲーム名</param>
/// <param name="CommandPrefix">チャットコマンドの接頭辞（例: pokech）</param>
/// <param name="Pokemon">登録したポケモン</param>
/// <param name="Teams">チーム</param>
/// <param name="NamesResolved">特性・持ち物の名前を取得元のデータで引けたか（false なら ID で表示している）</param>
public sealed record RosterGameView(string Key, string Name, string CommandPrefix,
    IReadOnlyList<RosterPokemonRow> Pokemon, IReadOnlyList<RosterTeamRow> Teams, bool NamesResolved)
{
    /// <summary>
    /// 選択欄に出す名前（例: ポケモンチャンピオンズ（!pokech））
    /// </summary>
    public string DisplayName => CommandPrefix.Length == 0 ? Name : $"{Name}（!{CommandPrefix}）";

    /// <summary>
    /// テラスタイプを登録したポケモンがいるか（いなければテラスの列を隠す）
    /// </summary>
    public bool HasTeraType => Pokemon.Any(p => p.TeraType.Length > 0);
}

/// <summary>
/// 登録したポケモンの 1 行
/// </summary>
public sealed record RosterPokemonRow(string Id, string Name, string Nature, string EVs, string Ability, string TeraType);

/// <summary>
/// チームの 1 行
/// </summary>
public sealed record RosterTeamRow(string Number, string Active, string Members);
