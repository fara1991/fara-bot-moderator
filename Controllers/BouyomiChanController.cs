using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 棒読みちゃん（BouyomiChan）との連携を制御するコントローラー
/// </summary>
public class BouyomiChanController
{
    /// <summary>
    /// 棒読みちゃんの HTTP 連携の待ち受け先（ポート 5008・/Talk）
    /// </summary>
    private const string TalkUrl = "http://localhost:5008/Talk";

    /// <summary>
    /// 発声の要求が時間内に返らないときの上限（棒読みちゃんが起動していないときに長く待たせないため）
    /// </summary>
    private static readonly TimeSpan TalkTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 起動確認の上限
    /// </summary>
    private static readonly TimeSpan RunningCheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 通信に使う HttpClient。呼び出しのたびに作ると、ソケットを使い切るので、1 つを使い回す（時間の制限は呼び出しごとに CancellationToken で付ける）
    /// </summary>
    private static readonly HttpClient Client = new();

    /// <summary>
    /// 棒読みちゃんに音声合成タスクを追加します。
    /// </summary>
    /// <param name="talkName">喋らせたい名前</param>
    /// <param name="talkText">喋らせたい文章</param>
    /// <param name="bouyomiChanCall">棒読みちゃんを呼び出すかどうか</param>
    public static void AddTalkTask(string talkName, string talkText, bool bouyomiChanCall)
    {
        if (bouyomiChanCall) _ = CreateTalkAsync($"{talkName}さん {talkText}");
    }

    /// <summary>
    /// イベント通知用の音声合成タスクを追加します。
    /// </summary>
    /// <param name="talkText">喋らせたい文章</param>
    /// <param name="bouyomiChanCall">棒読みちゃんを呼び出すかどうか</param>
    public static void AddEventTalkTask(string talkText, bool bouyomiChanCall)
    {
        if (bouyomiChanCall) _ = CreateTalkAsync(talkText);
    }

    /// <summary>
    /// 棒読みちゃんのHTTP連携APIを叩いて発声させます。
    /// </summary>
    /// <param name="talkText">喋らせたい文章</param>
    /// <returns>成功したかどうか</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "呼び出し元（AddTalkTask・AddEventTalkTask）は `_ =` で結果を待たない（fire-and-forget）ため、ここで受けないと失敗がどこにも記録されない。棒読みちゃんの失敗でボットを落とさないよう、種類を問わずログに残して続ける")]
    private static async Task<bool> CreateTalkAsync(string talkText)
    {
        try
        {
            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("text", talkText),
                new KeyValuePair<string, string>("speed", "-1"),
                new KeyValuePair<string, string>("tone", "-1"),
                new KeyValuePair<string, string>("volume", "-1"),
                new KeyValuePair<string, string>("voice", "0")
            ]);

            // タイムアウトを短めに設定（棒読みちゃんが起動していない場合に長時間待たされないようにするため）
            using var timeout = new CancellationTokenSource(TalkTimeout);
            using var response = await Client.PostAsync(TalkUrl, content, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // 棒読みちゃんとの通信に失敗してもアプリが落ちないようにログ出力のみ行う
            LogController.OutputLog($"BouyomiChan Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 棒読みちゃんが起動しているか確認します。
    /// </summary>
    /// <returns>起動していればtrue</returns>
    public static Task<bool> IsBouyomiChanRunningAsync() => IsRunningAsync(Client, RunningCheckTimeout);

    /// <summary>
    /// 棒読みちゃんに接続できるか確認します。通信の失敗・タイムアウトは「起動していない」として false を返し、それ以外の例外（コードの誤り）は呼び出し元へ出します。
    /// </summary>
    /// <param name="client">通信に使う HttpClient（テストで差し替える）</param>
    /// <param name="limit">確認の上限</param>
    /// <returns>接続できればtrue</returns>
    internal static async Task<bool> IsRunningAsync(HttpClient client, TimeSpan limit)
    {
        try
        {
            using var timeout = new CancellationTokenSource(limit);
            // 棒読みちゃんのHTTP連携が有効であれば、GET /Talk に対して (恐らく405 Method Not Allowed等が返るが) 接続はできるはず
            // 接続自体が拒否される場合は起動していないとみなす。応答の中身（ステータス）は見ない
            // 応答は使わない（接続できたことが分かればよい）。受け取った応答は捨てて、接続を返す
            (await client.GetAsync(TalkUrl, timeout.Token)).Dispose();
            return true;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            // 起動していないとみなすが、理由（接続の拒否・タイムアウト）はログに残す
            LogController.OutputLog($"BouyomiChan 接続確認: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// 棒読みちゃんに「つながらなかった」と言える例外か判定します。接続の拒否・通信の失敗（HttpRequestException）と、
    /// 時間切れ（OperationCanceledException。TaskCanceledException を含む）だけです。
    /// </summary>
    /// <param name="ex">捕まえた例外</param>
    /// <returns>つながらなかった例外ならtrue</returns>
    internal static bool IsConnectionFailure(Exception ex) => ex is HttpRequestException or OperationCanceledException;
}
