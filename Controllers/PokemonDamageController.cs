using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FaraPokemonAssistance.Core.Data;
using FaraPokemonAssistance.Core.Models;
using FaraPokemonAssistance.Core.Text;

namespace FaraBotModerator.Controllers;

/// <summary>
/// チャットコマンド「!dmg」でポケモンのダメージ計算を行うコントローラー
/// </summary>
public class PokemonDamageController
{
    /// <summary>
    /// コマンド名
    /// </summary>
    public const string CommandName = "!dmg";

    private const string DataUrl = "https://fara1991.github.io/fara-pokemon-assistance/data/";
    private const string DefaultDataSetKey = "Champions";
    private static readonly TimeSpan UserCooldown = TimeSpan.FromSeconds(5);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly DamageCommand _command;
    private readonly ConcurrentDictionary<string, DateTime> _lastExecutedAt = new();

    /// <summary>
    /// PokemonDamageController のコンストラクタ
    /// データは GitHub Pages の CSV を使い、ローカルに 1 日キャッシュします。
    /// </summary>
    public PokemonDamageController()
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FaraBotModerator", "pokemon-data");
        var source = new CachingDataSource(new HttpDataSource(HttpClient, DataUrl), cacheDirectory,
            TimeSpan.FromDays(1));
        _command = new DamageCommand(new DataCatalog(source));
    }

    /// <summary>
    /// メッセージが「!dmg」コマンドかどうかを判定します。
    /// </summary>
    /// <param name="message">チャットメッセージ</param>
    /// <returns>「!dmg」コマンドの場合は true</returns>
    public static bool IsCommand(string message)
    {
        var text = message.Trim();
        if (!text.StartsWith(CommandName, StringComparison.OrdinalIgnoreCase)) return false;
        // 「!dmgxxx」のような別コマンドは対象外
        return text.Length == CommandName.Length || char.IsWhiteSpace(text[CommandName.Length]);
    }

    /// <summary>
    /// 「!dmg ...」を計算し、チャットに返す 1 行を返します。
    /// 同一ユーザーのクールダウン中は null を返します。
    /// </summary>
    /// <param name="message">チャットメッセージ</param>
    /// <param name="userName">発言したユーザー名（クールダウン判定用）</param>
    /// <param name="maxLength">返信の最大長</param>
    /// <returns>返信メッセージ。返信しない場合は null</returns>
    public async Task<string?> HandleAsync(string message, string userName, int maxLength)
    {
        var now = DateTime.UtcNow;
        if (_lastExecutedAt.TryGetValue(userName, out var last) && now - last < UserCooldown) return null;
        _lastExecutedAt[userName] = now;

        var arguments = message.Trim()[CommandName.Length..];
        var result = await _command.ExecuteAsync(arguments, new DamageCommandOptions
        {
            DataSetKey = DefaultDataSetKey,
            Format = BattleFormat.Singles,
            MaxLength = maxLength
        });
        return result.Message;
    }
}
