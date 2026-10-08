using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonAssistance.Core.Data;
using FaraPokemonAssistance.Core.Models;
using FaraPokemonAssistance.Core.Roster;
using FaraPokemonAssistance.Core.Text;

namespace FaraBotModerator.Controllers;

/// <summary>
/// チャットコマンド「!poke」「!pokech」「!pokesv」「!pokess」でポケモンの登録・ダメージ計算などを行うコントローラー
/// </summary>
public class PokemonCommandController
{
    private const string DataUrl = "https://fara1991.github.io/fara-pokemon-assistance/data/";
    private static readonly TimeSpan UserCooldown = TimeSpan.FromSeconds(5);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// 連投対策のクールダウンを掛けるサブコマンド（計算系）
    /// </summary>
    private static readonly HashSet<string> CooldownSubCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dmg", "damage", "calc", "ev", "diff", "speed", "spd"
    };

    private static readonly char[] Separators = { ' ', '　', '\t', ',', '、' };

    private readonly PokeCommand _command;
    private readonly ConcurrentDictionary<string, DateTime> _lastExecutedAt = new();
    // 登録データ（RosterRepository）はスレッドセーフではないため、コマンドは 1 件ずつ処理する
    private readonly SemaphoreSlim _executeLock = new(1, 1);

    /// <summary>
    /// PokemonCommandController のコンストラクタ
    /// データは GitHub Pages の CSV を使い、ローカルに 1 日キャッシュします。
    /// 登録したポケモン・チームは %LOCALAPPDATA%\FaraBotModerator\pokemon-roster.json に保存します。
    /// </summary>
    public PokemonCommandController()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaraBotModerator");
        var source = new CachingDataSource(new HttpDataSource(HttpClient, DataUrl),
            Path.Combine(appDirectory, "pokemon-data"), TimeSpan.FromDays(1));
        var roster = new RosterRepository(new FileRosterStore(Path.Combine(appDirectory, "pokemon-roster.json")));
        _command = new PokeCommand(new DataCatalog(source), roster);
    }

    /// <summary>
    /// メッセージがポケモンコマンドかどうかを判定します。
    /// データの取得に失敗した場合はコマンドではないものとして扱います。
    /// </summary>
    /// <param name="message">チャットメッセージ</param>
    /// <returns>ポケモンコマンドの場合は true</returns>
    public async Task<bool> IsCommandAsync(string message)
    {
        if (!message.TrimStart().StartsWith('!')) return false;
        try
        {
            return await _command.MatchAsync(message) is not null;
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> Pokemon data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ポケモンコマンドを実行し、チャットに返す 1 行を返します。
    /// 計算系コマンドで同一ユーザーのクールダウン中は null を返します。
    /// </summary>
    /// <param name="message">チャットメッセージ</param>
    /// <param name="userName">発言したユーザーのログイン名</param>
    /// <param name="isBroadcaster">配信者本人かどうか</param>
    /// <param name="isModerator">モデレーターかどうか</param>
    /// <param name="maxLength">返信の最大長</param>
    /// <returns>返信メッセージ。返信しない場合は null</returns>
    public async Task<string?> HandleAsync(string message, string userName, bool isBroadcaster, bool isModerator,
        int maxLength)
    {
        if (IsCooldownTarget(message))
        {
            var now = DateTime.UtcNow;
            if (_lastExecutedAt.TryGetValue(userName, out var last) && now - last < UserCooldown) return null;
            _lastExecutedAt[userName] = now;
        }

        // 登録・削除・使用チーム変更は配信者本人とモデレーターのみ（視聴者は閲覧・計算系のみ）
        var canEdit = isBroadcaster || isModerator;

        await _executeLock.WaitAsync();
        try
        {
            var result = await _command.ExecuteAsync(message, new PokeCommandOptions
            {
                Format = BattleFormat.Singles,
                AllowMutations = canEdit,
                MaxLength = maxLength
            });
            return result.Message;
        }
        finally
        {
            _executeLock.Release();
        }
    }

    /// <summary>
    /// 「!pokech dmg ...」のように 2 語目が計算系サブコマンドかどうかを判定します。
    /// </summary>
    private static bool IsCooldownTarget(string message)
    {
        var tokens = message.Split(Separators, 3, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 && CooldownSubCommands.Contains(tokens[1]);
    }
}
