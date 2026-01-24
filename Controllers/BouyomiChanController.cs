using System.Collections.Generic;
using System.Net.Http;

namespace FaraBotModerator.Controllers;

/// <summary>
/// </summary>
public class BouyomiChanController
{
    /// <summary>
    ///     棒読みちゃんに音声合成タスクを追加します。
    /// </summary>
    /// <param name="talkName">喋らせたい名前</param>
    /// <param name="talkText">喋らせたい文章</param>
    /// <param name="bouyomiChanCall"></param>
    public bool AddTalkTask(string talkName, string talkText, bool bouyomiChanCall)
    {
        if (bouyomiChanCall) return CreateTalk($"{talkName}さん {talkText}");
        return true;
    }

    /// <summary>
    /// </summary>
    /// <param name="talkText"></param>
    /// <param name="bouyomiChanCall"></param>
    public bool AddEventTalkTask(string talkText, bool bouyomiChanCall)
    {
        if (bouyomiChanCall) return CreateTalk(talkText);
        return true;
    }

    private bool CreateTalk(string talkText)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("text", talkText),
                new KeyValuePair<string, string>("speed", "-1"),
                new KeyValuePair<string, string>("tone", "-1"),
                new KeyValuePair<string, string>("volume", "-1"),
                new KeyValuePair<string, string>("voice", "0")
            });

            using var client = new HttpClient();
            // タイムアウトを短めに設定（棒読みちゃんが起動していない場合に長時間待たされないようにするため）
            client.Timeout = System.TimeSpan.FromSeconds(2);
            var response = client.PostAsync("http://localhost:5008/Talk", content).Result;
            return response.IsSuccessStatusCode;
        }
        catch (System.Exception ex)
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
    public bool IsBouyomiChanRunning()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = System.TimeSpan.FromSeconds(2);
            // 棒読みちゃんのHTTP連携が有効であれば、GET /Talk に対して (恐らく405 Method Not Allowed等が返るが) 接続はできるはず
            // 接続自体が拒否される場合は起動していないとみなす
            var response = client.GetAsync("http://localhost:5008/Talk").Result;
            return true;
        }
        catch
        {
            return false;
        }
    }
}