namespace FaraBotModeratorSite.Components.Layout;

/// <summary>フッターのリンクの組み立て（SiteFooter.razor。テストできるよう、画面から分けている）。</summary>
public static class SiteFooterLinks
{
    /// <summary>
    /// 「作者へ報告」のリンク。見ているページのパスを ?from= に付ける（どのページの報告かが分かるように）。
    /// 報告ページ・プライバシーポリシーのとき、英数字・- _ / . 以外を含むパスのときは付けない（worker/report.js と report.js が同じ条件で受け付ける）。
    /// </summary>
    public static string ReportHref(string path)
    {
        if (path is "/report" or "/privacy" || path.Length == 0 || path.Length > 300 || path[0] != '/' || path.StartsWith("//")) return "/report";
        return path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')
            ? "/report?from=" + Uri.EscapeDataString(path)
            : "/report";
    }
}
