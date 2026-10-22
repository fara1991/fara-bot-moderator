using FaraBotModeratorSite.Data;

namespace FaraBotModeratorSite.Components.Layout;

/// <summary>フッターの SNS アイコン 1 つ分。Service は SocialIcon の名前（github / twitter / twitch）。</summary>
public sealed record SocialLink(string Service, string Label, string Url);

/// <summary>フッターのリンクの組み立て（SiteFooter.razor。テストできるよう、画面から分けている）。</summary>
public static class SiteFooterLinks
{
    /// <summary>
    /// 「問い合わせ」のリンク。見ているページのパスを ?from= に付ける（どのページの問い合わせかが分かるように）。
    /// 問い合わせページ・プライバシーポリシーのとき、英数字・- _ / . 以外を含むパスのときは付けない（worker/report.js と report.js が同じ条件で受け付ける）。
    /// </summary>
    public static string ReportHref(string path)
    {
        if (path is "/report" or "/privacy" || path.Length == 0 || path.Length > 300 || path[0] != '/' || path.StartsWith("//")) return "/report";
        return path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')
            ? "/report?from=" + Uri.EscapeDataString(path)
            : "/report";
    }

    /// <summary>SNS のアイコン（URL が空のものは出さない）。順番は GitHub・Twitter・Twitch。</summary>
    public static IReadOnlyList<SocialLink> Socials(SiteOptions options) => new[]
    {
        new SocialLink("github", "GitHub", options.GitHubUrl),
        new SocialLink("twitter", "Twitter", options.TwitterUrl),
        new SocialLink("twitch", "Twitch", options.TwitchUrl),
    }.Where(s => !string.IsNullOrWhiteSpace(s.Url)).ToList();
}
