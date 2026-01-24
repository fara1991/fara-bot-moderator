using DeepL;
using DeepL.Model;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchLib.Client;
using FaraBotModerator.Properties;

namespace FaraBotModerator.Controllers;

/// <summary>
/// DeepL APIを用いた翻訳処理を担当するコントローラー
/// </summary>
public class TwitchTranslationController : System.IDisposable
{
    private readonly Translator _deepLTranslator;

    /// <summary>
    /// TwitchTranslationController のコンストラクタ
    /// </summary>
    /// <param name="deepLApiKey">DeepL APIキー</param>
    public TwitchTranslationController(string deepLApiKey)
    {
        _deepLTranslator = new Translator(deepLApiKey);
    }

    /// <summary>
    /// 指定されたテキストを翻訳します。
    /// </summary>
    /// <param name="text">翻訳対象テキスト</param>
    /// <param name="targetLanguageCode">ターゲット言語コード（例: "EN-US", "JA"）</param>
    /// <returns>翻訳結果モデル</returns>
    public async Task<TextResult> TranslateAsync(string text, string targetLanguageCode)
    {
        return await _deepLTranslator.TranslateTextAsync(text, null, targetLanguageCode);
    }

    /// <summary>
    /// DeepL APIの使用状況を取得します。
    /// </summary>
    /// <returns>使用状況情報</returns>
    public async Task<Usage> GetUsageAsync()
    {
        return await _deepLTranslator.GetUsageAsync();
    }

    /// <summary>
    /// テストが日本語を含んでいるかどうかを判定します。
    /// </summary>
    /// <param name="text">判定対象テキスト</param>
    /// <returns>日本語を含んでいればtrue</returns>
    public bool IsJapanese(string text)
    {
        return Regex.IsMatch(text, @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF]");
    }

    /// <summary>
    /// メッセージ内からTwitchのエモート文字列（URL形式に変換されたものも含む）を削除します。
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <param name="twitchClient">Twitchクライアントインスタンス</param>
    /// <returns>エモート削除後のメッセージ</returns>
    public string RemoveEmotes(string message, TwitchClient twitchClient)
    {
        var replaceEmoteMessage = twitchClient.ChannelEmotes.ReplaceEmotes(message);
        var deleteEmoteMessage =
            Regex.Replace(replaceEmoteMessage, "https://static-cdn.jtvnw.net/emoticons/v1/.*?/[0-9].0", "");
        return deleteEmoteMessage.Trim();
    }

    /// <summary>
    /// メッセージがURLのみで構成されているかどうかを判定します。
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <returns>URLのみであればtrue</returns>
    public bool IsOnlyUrl(string message)
    {
        return message.Split(" ").Length == 1 &&
               Regex.IsMatch(
                   message,
                   "^(http|https):\\/\\/[a-zA-Z0-9-]+(\\.[a-zA-Z0-9-]+)*(\\/[^\\s]*)?$"
               );
    }

    /// <summary>
    /// 指定されたメッセージが翻訳対象の単語であるかを判定します（ボット名、特定のコマンド、URLのみなどは除外）。
    /// </summary>
    /// <param name="message">判定対象メッセージ</param>
    /// <returns>翻訳対象であればtrue</returns>
    public bool IsTargetTranslationWord(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        if (message.Contains(Settings.Default.BotName)) return false;
        if (message.Contains("cheer")) return false;
        if (message.Contains("!bomb")) return false;
        if (IsOnlyUrl(message)) return false;
        return true;
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        _deepLTranslator.Dispose();
    }
}
