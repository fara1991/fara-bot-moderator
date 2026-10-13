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
    /// 本番のポケモンのデータ（CSV）の URL（https://pokemon.fara-labs.com/api/game/ 。認証なしの静的ファイルで、サイトの Worker は通らない）
    /// </summary>
    private const string ProductionDataUrl = RemoteRosterStore.ProductionBaseUrl + HttpDataSource.GameDataPath;

    /// <summary>
    /// 開発用のポケモンのデータ（CSV）の URL（https://pokemon-dev.fara-labs.com/api/game/ ）。
    /// pokemon-dev は Cloudflare Access で保護されているため、/api/*（ゲームデータの /api/game/* と登録データの /api/my/*）を Access の対象外（Bypass）にしておく必要があります。
    /// </summary>
    private const string DevelopDataUrl = RemoteRosterStore.DevelopBaseUrl + HttpDataSource.GameDataPath;
    private static readonly TimeSpan UserCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// データ・登録データの取得に使う HttpClient
    /// </summary>
    internal static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>
    /// 計算系のサブコマンド。連投対策のクールダウンを掛け、登録データ（使用チーム）を読めなくても計算は返します
    /// </summary>
    private static readonly HashSet<string> CalcSubCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dmg", "damage", "calc", "ev", "faster"
    };

    /// <summary>
    /// アクセスキーが無いときに登録系のコマンドで返すメッセージ
    /// </summary>
    public const string MissingSyncKeyMessage = "アクセスキーが設定されていません（Auto Bot Settings > Game で設定してください）";

    /// <summary>
    /// 計算系コマンドで使用チームを読むときの待ち時間。クラウドに届かなくても計算は返すため、長くは待たない
    /// </summary>
    private static readonly TimeSpan CalcRosterTimeout = TimeSpan.FromSeconds(10);

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

    /// <summary>
    /// ボットのデータの保存先（%LOCALAPPDATA%\FaraBotModerator）。ポケモンのデータ（CSV）のキャッシュを置きます
    /// </summary>
    public static readonly string AppDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaraBotModerator");

    // 登録・閲覧系のコマンド（登録データを読めなければ失敗を返す）と、計算系のコマンド（読めなければ使用チームなしで計算する）
    private volatile PokeCommand _command;
    private volatile PokeCommand _calcCommand;
    private PokemonDataSource _dataSource;
    private string _syncKey;
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
    /// 登録したポケモン・チームはアクセスキーで取得元のサイトのクラウドに読み書きします（この PC には保存しません）。
    /// </summary>
    /// <param name="format">ダメージ計算に使う対戦形式</param>
    /// <param name="dataSource">データの取得元（登録データのクラウドも同じサイト）</param>
    /// <param name="syncKey">登録データのアクセスキー（空なら登録系のコマンドは使えません）</param>
    public PokemonCommandController(BattleFormat format, PokemonDataSource dataSource, string syncKey)
    {
        Format = format;
        _dataSource = dataSource;
        _syncKey = syncKey;
        (_command, _calcCommand) = CreateCommands(dataSource, syncKey);
    }

    /// <summary>
    /// データの取得元。接続中でも切り替えられ、次のコマンドから切り替え先のデータと登録データを使います。
    /// </summary>
    public PokemonDataSource DataSource
    {
        get => _dataSource;
        set
        {
            if (_dataSource == value) return;
            _dataSource = value;
            (_command, _calcCommand) = CreateCommands(value, _syncKey);
        }
    }

    /// <summary>
    /// 登録データのアクセスキー。接続中でも切り替えられ、次のコマンドから切り替え先のキーを使います。
    /// </summary>
    public string SyncKey
    {
        get => _syncKey;
        set
        {
            if (_syncKey == value) return;
            _syncKey = value;
            (_command, _calcCommand) = CreateCommands(_dataSource, value);
        }
    }

    /// <summary>
    /// 指定した取得元・アクセスキーを使うコマンドを作成します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <param name="syncKey">アクセスキー</param>
    /// <returns>登録・閲覧系のコマンドと計算系のコマンド（データは共有）</returns>
    private static (PokeCommand Command, PokeCommand CalcCommand) CreateCommands(PokemonDataSource dataSource, string syncKey)
    {
        var catalog = CreateCatalog(dataSource);
        return (new PokeCommand(catalog, new RosterRepository(CreateRosterStore(dataSource, syncKey))),
            new PokeCommand(catalog, new RosterRepository(
                new BestEffortRosterStore(CreateRosterStore(dataSource, syncKey), CalcRosterTimeout))));
    }

    /// <summary>
    /// 指定した取得元のデータ（CSV）を読むカタログを作成します。キャッシュは取得元ごとに別のフォルダに保存します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    /// <returns>データカタログ</returns>
    public static DataCatalog CreateCatalog(PokemonDataSource dataSource)
    {
        var (url, cacheFolder) = dataSource == PokemonDataSource.Develop
            ? (DevelopDataUrl, "pokemon-data-dev")
            : (ProductionDataUrl, "pokemon-data");
        var source = new CachingDataSource(new HttpDataSource(HttpClient, url),
            Path.Combine(AppDirectory, cacheFolder), TimeSpan.FromDays(1));
        return new DataCatalog(source, TimeSpan.FromDays(1));
    }

    /// <summary>
    /// 取得元のサイトの URL（登録データのクラウドもこのサイトの /api/my/pokemon・/api/my/team）を返します。
    /// </summary>
    /// <param name="dataSource">データの取得元</param>
    public static string SiteBaseUrl(PokemonDataSource dataSource)
    {
        return dataSource == PokemonDataSource.Develop ? RemoteRosterStore.DevelopBaseUrl : RemoteRosterStore.ProductionBaseUrl;
    }

    /// <summary>
    /// 登録データの保存先を作成します。アクセスキーが無い（形式が違う）ときは、読み書きのたびに
    /// 「アクセスキーが設定されていません」の <see cref="RosterUnavailableException"/> を投げる保存先を返します。
    /// </summary>
    /// <param name="dataSource">データの取得元（同じサイトのクラウドを使う）</param>
    /// <param name="syncKey">アクセスキー</param>
    public static IRosterStore CreateRosterStore(PokemonDataSource dataSource, string? syncKey)
    {
        return RemoteRosterStore.IsValidKey(syncKey)
            ? new RemoteRosterStore(HttpClient, SiteBaseUrl(dataSource), syncKey!)
            : new MissingSyncKeyRosterStore();
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

            // 計算系はクールダウンを掛け、登録データ（使用チーム）を読めなくても使用チームなしで計算する
            var isCalc = IsCalcSubCommand(match.Value.Arguments);
            if (isCalc)
            {
                // クールダウンの確認と記録はロック内で行い、同一ユーザーの同時投稿がすり抜けないようにする
                var now = DateTime.UtcNow;
                if (_lastExecutedAt.TryGetValue(userName, out var last) && now - last < UserCooldown)
                    return (true, null);
                _lastExecutedAt[userName] = now;
                command = _calcCommand;
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
    private static bool IsCalcSubCommand(string arguments)
    {
        var tokens = arguments.Split(Separators, 2, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 1 && CalcSubCommands.Contains(tokens[0]);
    }

    /// <summary>
    /// アクセスキーが無いときの登録データの保存先。読み書きのたびに「アクセスキーが設定されていません」を投げます
    /// （PokeCommand が「失敗しました: アクセスキーが設定されていません（…）」としてチャットに返します）。
    /// </summary>
    private sealed class MissingSyncKeyRosterStore : IRosterStore
    {
        public bool AlwaysReload => true;

        public Task<string?> LoadAsync(CancellationToken ct = default) => throw Missing();

        public Task SaveAsync(string json, CancellationToken ct = default) => throw Missing();

        public Task SavePartAsync(RosterState state, string dataSetKey, RosterPart part, CancellationToken ct = default) =>
            throw Missing();

        private static RosterUnavailableException Missing() =>
            new(RosterUnavailableReason.Unauthorized, null, MissingSyncKeyMessage);
    }

    /// <summary>
    /// 計算系コマンド用の登録データの保存先。使用チームを読めない（アクセスキーが無い・クラウドに届かない）ときは
    /// 登録なしとして扱い、計算は使用チームなしで続けます。計算系は登録データを書き換えません。
    /// </summary>
    private sealed class BestEffortRosterStore(IRosterStore inner, TimeSpan timeout) : IRosterStore
    {
        public bool AlwaysReload => true;

        public async Task<string?> LoadAsync(CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await inner.LoadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (RosterUnavailableException ex)
            {
                // メッセージにアクセスキーは含まれない
                if (ex.Message != MissingSyncKeyMessage)
                    LogController.OutputLog($"<Warning> Pokemon roster (計算は使用チームなしで続行): {ex.Message}");
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LogController.OutputLog("<Warning> Pokemon roster: クラウドの応答が遅いため、使用チームなしで計算します");
                return null;
            }
        }

        public Task SaveAsync(string json, CancellationToken ct = default) => inner.SaveAsync(json, ct);

        // 変えた部分（ポケモン / チーム）だけを保存する経路は、そのまま中の保存先に渡す
        public Task SavePartAsync(RosterState state, string dataSetKey, RosterPart part, CancellationToken ct = default) =>
            inner.SavePartAsync(state, dataSetKey, part, ct);
    }
}

/// <summary>
/// ポケモンのデータの取得元
/// </summary>
public enum PokemonDataSource
{
    /// <summary>
    /// 本番（https://pokemon.fara-labs.com/）
    /// </summary>
    Production,

    /// <summary>
    /// 開発用（https://pokemon-dev.fara-labs.com/）
    /// </summary>
    Develop
}
