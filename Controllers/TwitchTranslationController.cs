using DeepL;
using DeepL.Model;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchLib.Client;
using FaraBotModerator.Properties;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 翻訳処理を担当するコントローラー
/// </summary>
public class TwitchTranslationController : System.IDisposable
{
    private readonly Translator _deepLTranslator;

    public TwitchTranslationController(string deepLApiKey)
    {
        _deepLTranslator = new Translator(deepLApiKey);
    }

    public async Task<TextResult> TranslateAsync(string text, string targetLanguageCode)
    {
        return await _deepLTranslator.TranslateTextAsync(text, null, targetLanguageCode);
    }

    public async Task<Usage> GetUsageAsync()
    {
        return await _deepLTranslator.GetUsageAsync();
    }

    public bool IsJapanese(string text)
    {
        return Regex.IsMatch(text, @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF]");
    }

    public string RemoveEmotes(string message, TwitchClient twitchClient)
    {
        var replaceEmoteMessage = twitchClient.ChannelEmotes.ReplaceEmotes(message);
        var deleteEmoteMessage =
            Regex.Replace(replaceEmoteMessage, "https://static-cdn.jtvnw.net/emoticons/v1/.*?/[0-9].0", "");
        return deleteEmoteMessage.Trim();
    }

    public bool IsOnlyUrl(string message)
    {
        return message.Split(" ").Length == 1 &&
               Regex.IsMatch(
                   message,
                   "^(http|https):\\/\\/[a-zA-Z0-9-]+(\\.[a-zA-?0-9-]+)*(\\/[^\\s]*)?$"
               );
    }

    public bool IsTargetTranslationWord(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        if (message.Contains(Settings.Default.BotName)) return false;
        if (message.Contains("cheer")) return false;
        if (message.Contains("!bomb")) return false;
        if (IsOnlyUrl(message)) return false;
        return true;
    }

    public void Dispose()
    {
        _deepLTranslator.Dispose();
    }
}
