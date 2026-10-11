using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonBattleAssistant.Core.Data;
using FaraPokemonBattleAssistant.Core.Models;
using FaraPokemonBattleAssistant.Core.Roster;
using FaraPokemonBattleAssistant.Core.Text;

namespace FaraBotModerator.Controllers;

/// <summary>
/// チャットコマンド「!poke」「!pokech」「!pokesv」「!pokess」でポケモンの登録・ダメージ計算などを行うコントローラー
/// </summary>
public class PokemonCommandController
{
    /// <summary>
    /// 本番のポケモンのデータ（CSV）の URL
    /// </summary>
    private const string ProductionDataUrl = "https://pokemon.fara-labs.com/data/";

    /// <summary>
    /// 開発用のポケモンのデータ（CSV）の URL。
    /// pokemon-dev は Cloudflare Access で保護されているため、/data/ を Access の対象外（Bypass）にしておく必要があります。
    /// </summary>
    private const string DevelopDataUrl = "https://pokemon-dev.fara-labs.com/data/";
    private static readonly TimeSpan UserCooldown = TimeSpan.FromSeconds(5);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>
    /// 連投対策のクールダウンを掛けるサブコマンド（計算系）
    /// </summary>
    private static readonly HashSet<string> CooldownSubCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dmg", "damage", "calc", "ev", "diff", "speed", "spd"
    };

    /// <summary>
    /// データ取得用の HttpClient を作成します。
    /// User-Agent が無いと Cloudflare のブラウザ整合性チェックで拒否されることがあるため、明示的に付けます。
    /// リダイレクトは追わないので、Cloudflare Access のログイン画面への転送は（HTML をデータとして読まずに）失敗として扱われます。
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FaraBotModerator (+https://github.com/fara1991/fara-bot-moderator)");
        return client;
    }

    private const string CommandPrefix = "!poke";

    private static readonly char[] Separators = { ' ', '　', '\t', ',', '、' };

    private readonly string _appDirectory;
    private readonly RosterRepository _roster;
    private volatile PokeCommand _command;
    private PokemonDataSource _dataSource;
    private readonly ConcurrentDictionary<string, DateTime> _lastExecutedAt = new();
    // 登録データ（RosterRepository）はスレッドセーフではないため、コマンドは 1 件ずつ処理する
    private readonly SemaphoreSlim _executeLock = new(1, 1);

    /// <summary>
    /// ダメージ計算に使う対戦形式。接続中でも切り替えられます。
    /// </summary>
    public BattleFormat Format { get; set; }

    /// <summary>
    /// PokemonCommandController のコンストラクタ
    /// データは Cloudflare に公開した CSV（本番か開発用のどちらか）を使い、ローカルに 1 日キャッシュします（起動中も 1 日ごとに読み直します）。
    /// 登録したポケモン・チームは %LOCALAPPDATA%\FaraBotModerator\pokemon-roster.json に保存します。
    /// </summary>
    /// <param name="format">ダメージ計算に使う対戦形式</param>
    /// <param name="dataSource">データの取得元</param>
    public PokemonCommandController(BattleFormat format, PokemonDataSource dataSource)
    {
        Format = format;
        _appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaraBotModerator");
        _roster = new RosterRepository(new FileRosterStore(Path.Combine(_appDirectory, "pokemon-roster.json")));
        _dataSource = dataSource;
        _command = CreateCommand(dataSource);
    }

    /// <summary>
    /// データの取得元。接続中でも切り替えられ、次のコマンドから切り替え先のデータを使います。
    /// </summary>
    public PokemonDataSource DataSource
    {
        get => _dataSource;
        set
        {
            if (_dataSource == value) return;
            _dataSource = value;
            _command = CreateCommand(value);
        }
    }

    /// <summary>
    /// 指定した取得元のデータを使うコマンドを作成します。キャッシュは取得元ごとに別のフォルダに保存します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <returns>ポケモンコマンド</returns>
    private PokeCommand CreateCommand(PokemonDataSource dataSource)
    {
        var (url, cacheFolder) = dataSource == PokemonDataSource.Develop
            ? (DevelopDataUrl, "pokemon-data-dev")
            : (ProductionDataUrl, "pokemon-data");
        var source = new CachingDataSource(new HttpDataSource(HttpClient, url),
            Path.Combine(_appDirectory, cacheFolder), TimeSpan.FromDays(1));
        return new PokeCommand(new DataCatalog(source, TimeSpan.FromDays(1)), _roster);
    }

    /// <summary>
    /// 設定ファイルの文字列をデータの取得元に変換します。不正な値は本番として扱います。
    /// </summary>
    /// <param name="value">"Production" または "Develop"</param>
    /// <returns>データの取得元</returns>
    public static PokemonDataSource ParseDataSource(string? value)
    {
        return Enum.TryParse<PokemonDataSource>(value, true, out var source) ? source : PokemonDataSource.Production;
    }

    /// <summary>
    /// 設定ファイルの文字列を対戦形式に変換します。不正な値はシングルとして扱います。
    /// </summary>
    /// <param name="value">"Singles" または "Doubles"</param>
    /// <returns>対戦形式</returns>
    public static BattleFormat ParseFormat(string? value)
    {
        return Enum.TryParse<BattleFormat>(value, true, out var format) ? format : BattleFormat.Singles;
    }

    /// <summary>
    /// ポケモンコマンドであれば実行し、チャットに返す 1 行を返します。
    /// コマンドかどうかを先に判定し、コマンドでないメッセージ（「!pokedex ...」など）にはクールダウンを掛けません。
    /// データの取得に失敗した場合はコマンドではないものとして扱い（翻訳などの通常の処理に回す）、次回のコマンドで取り直します。
    /// </summary>
    /// <param name="message">チャットメッセージ</param>
    /// <param name="userName">発言したユーザーのログイン名</param>
    /// <param name="isBroadcaster">配信者本人かどうか</param>
    /// <param name="isModerator">モデレーターかどうか</param>
    /// <param name="maxLength">返信の最大長</param>
    /// <returns>Handled: ポケモンコマンドとして処理したか。Reply: 返信メッセージ（計算系コマンドのクールダウン中は null）</returns>
    public async Task<(bool Handled, string? Reply)> HandleAsync(string message, string userName, bool isBroadcaster,
        bool isModerator, int maxLength)
    {
        // 接頭辞はすべて「!poke」で始まるため、それ以外のメッセージではデータの取得（初回は HTTP）を行わない
        if (!message.TrimStart().StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase)) return (false, null);

        // 登録・削除・使用チーム変更は配信者本人とモデレーターのみ（視聴者は閲覧・計算系のみ）
        var canEdit = isBroadcaster || isModerator;

        await _executeLock.WaitAsync();
        try
        {
            // 「!pokedex ...」のように「!poke」で始まるだけのメッセージはコマンドではないので、
            // クールダウンより先に（実行はせずに）コマンドかどうかを判定する。照合ではデータセット一覧だけを読む
            var command = _command;
            var match = await command.MatchAsync(message);
            if (match is null) return (false, null);

            // クールダウンの確認と記録はロック内で行い、同一ユーザーの同時投稿がすり抜けないようにする
            if (IsCooldownTarget(match.Value.Arguments))
            {
                var now = DateTime.UtcNow;
                if (_lastExecutedAt.TryGetValue(userName, out var last) && now - last < UserCooldown)
                    return (true, null);
                _lastExecutedAt[userName] = now;
            }

            var result = await command.TryExecuteAsync(message, new PokeCommandOptions
            {
                Format = Format,
                AllowMutations = canEdit,
                MaxLength = maxLength
            });
            return result is null ? (false, null) : (true, result.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogController.OutputLog($"<Error> Pokemon data: {ex.Message}");
            return (false, null);
        }
        finally
        {
            _executeLock.Release();
        }
    }

    /// <summary>
    /// 「!pokech dmg ...」のように、接頭辞の後の 1 語目が計算系サブコマンドかどうかを判定します。
    /// </summary>
    /// <param name="arguments">接頭辞を除いた残り（「dmg ...」）</param>
    private static bool IsCooldownTarget(string arguments)
    {
        var tokens = arguments.Split(Separators, 2, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 1 && CooldownSubCommands.Contains(tokens[0]);
    }
}

/// <summary>
/// ポケモンのデータの取得元
/// </summary>
public enum PokemonDataSource
{
    /// <summary>
    /// 本番（https://pokemon.fara-labs.com/data/）
    /// </summary>
    Production,

    /// <summary>
    /// 開発用（https://pokemon-dev.fara-labs.com/data/）
    /// </summary>
    Develop
}
