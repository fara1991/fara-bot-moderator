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
public partial class TwitchTranslationController : System.IDisposable
{
    private readonly Translator? _deepLTranslator;

    /// <summary>
    /// TwitchTranslationController のコンストラクタ
    /// </summary>
    /// <param name="deepLApiKey">DeepL APIキー</param>
    public TwitchTranslationController(string deepLApiKey)
    {
        if (!string.IsNullOrEmpty(deepLApiKey))
        {
            _deepLTranslator = new Translator(deepLApiKey);
        }
    }

    /// <summary>
    /// 指定されたテキストを翻訳します。
    /// </summary>
    /// <param name="text">翻訳対象テキスト</param>
    /// <param name="targetLanguageCode">ターゲット言語コード（例: "EN-US", "JA"）</param>
    /// <returns>翻訳結果モデル</returns>
    public async Task<TextResult?> TranslateAsync(string text, string targetLanguageCode)
    {
        if (_deepLTranslator == null) return null;
        return await _deepLTranslator.TranslateTextAsync(text, null, targetLanguageCode);
    }

    /// <summary>
    /// テストが日本語を含んでいるかどうかを判定します。
    /// </summary>
    /// <param name="text">判定対象テキスト</param>
    /// <returns>日本語を含んでいればtrue</returns>
    public static bool IsJapanese(string text)
    {
        return JapaneseRegex().IsMatch(text);
    }

    /// <summary>
    /// メッセージ内からTwitchのエモート文字列（URL形式に変換されたものも含む）を削除します。
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <param name="twitchClient">Twitchクライアントインスタンス</param>
    /// <returns>エモート削除後のメッセージ</returns>
    public static string RemoveEmotes(string message, TwitchClient twitchClient)
    {
        var replaceEmoteMessage = twitchClient.ChannelEmotes.ReplaceEmotes(message);
        var deleteEmoteMessage = EmoteRegex().Replace(replaceEmoteMessage, "");
        return deleteEmoteMessage.Trim();
    }

    /// <summary>
    /// メッセージがURLのみで構成されているかどうかを判定します。
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <returns>URLのみであればtrue</returns>
    public static bool IsOnlyUrl(string message)
    {
        return message.Split(" ").Length == 1 && UrlRegex().IsMatch(message);
    }

    /// <summary>
    /// 指定されたメッセージが翻訳対象の単語であるかを判定します（ボット名、特定のコマンド、URLのみなどは除外）。
    /// </summary>
    /// <param name="message">判定対象メッセージ</param>
    /// <returns>翻訳対象であればtrue</returns>
    public static bool IsTargetTranslationWord(string message)
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
        _deepLTranslator?.Dispose();
    }

    [GeneratedRegex(@"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF]")]
    private static partial Regex JapaneseRegex();
    
    [GeneratedRegex("^(http|https):\\/\\/[a-zA-Z0-9-]+(\\.[a-zA-Z0-9-]+)*(\\/[^\\s]*)?$")]
    private static partial Regex UrlRegex();
    [GeneratedRegex("https://static-cdn.jtvnw.net/emoticons/v1/.*?/[0-9].0")]
    private static partial Regex EmoteRegex();
}