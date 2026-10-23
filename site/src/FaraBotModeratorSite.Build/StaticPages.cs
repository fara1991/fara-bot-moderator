using System.Text.Json.Nodes;
using FaraBotModeratorSite.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FaraBotModeratorSite.Build;

/// <summary>
/// 検索エンジン・JavaScript なしの環境のための、本文入りの静的 HTML のもと（fara-site-kit の build-pages の pages.json）。
/// 本文は、ブラウザで動くのと同じページの部品（Pages）をここで描いて作る。ブラウザは起動すると、この本文を同じ内容の画面に置き換える。
/// </summary>
public static class StaticPages
{
    /// <summary>本文の先頭の要素に付ける class（build-pages の site.json の bodyRootClass。読み込み表示の JS が、この class の間だけ本文を操作できなくする）。</summary>
    public const string BodyRootClass = "entry-static";

    /// <summary>build-pages の pages.json（{ "pages": [...] }）を作る。</summary>
    public static async Task<JsonObject> BuildPagesJsonAsync(SiteContent content)
    {
        var pages = new JsonArray();
        pages.Add(Page("/", PageMeta.HomeTitle, PageMeta.HomeDescription, await RenderAsync<Home>(content)));
        pages.Add(Page("/docs", PageMeta.DocsTitle, PageMeta.DocsDescription, await RenderAsync<Docs>(content)));
        foreach (var doc in content.Pages)
        {
            pages.Add(Page(doc.Url, PageMeta.DocTitle(doc), doc.Description,
                await RenderAsync<Doc>(content, new Dictionary<string, object?> { ["Slug"] = doc.Slug })));
        }
        pages.Add(Page("/changelog", PageMeta.ChangelogTitle, PageMeta.ChangelogDescription, await RenderAsync<Changelog>(content)));
        pages.Add(Page("/privacy", PageMeta.PrivacyTitle, PageMeta.PrivacyDescription, await RenderAsync<Privacy>(content)));
        // 問い合わせのフォームは、ブラウザ（MudBlazor + Turnstile）でだけ動く。検索には出さない（noindex。サイトマップにも入れない）ので、静的な本文は見出しと案内だけ
        pages.Add(Page("/report", PageMeta.ReportTitle, PageMeta.ReportDescription, ReportBody, noindex: true));
        return new JsonObject { ["pages"] = pages };
    }

    /// <summary>存在しない URL に返す 404.html の中身（build-pages の site.json の notFoundPage）。ブラウザは起動すると、kit の NotFoundView に置き換える。</summary>
    public static JsonObject NotFoundPage() => new()
    {
        ["title"] = PageMeta.NotFoundTitle,
        ["bodyHtml"] = Wrap(
            "<section class=\"fsk-not-found\">" +
            "<p class=\"fsk-not-found-code\" aria-hidden=\"true\">404</p>" +
            "<h1>ページが見つかりません</h1>" +
            "<p class=\"fsk-not-found-lead\">URL が間違っているか、ページが移動した可能性があります。</p>" +
            "<a class=\"mud-button-root mud-button mud-button-filled mud-button-filled-primary mud-button-filled-size-medium fsk-btn-primary\" href=\"/\">ホームへ戻る</a>" +
            "</section>"),
    };

    private const string ReportBodyInner =
        "<section class=\"fsk-page-header fsk-page-column\">" +
        "<h1>問い合わせ</h1>" +
        "<p class=\"fsk-muted\">不具合・要望・ご意見を、作者に直接送れます。いただいた内容は、作者に通知されます（公開はされません）。</p>" +
        "</section>" +
        "<p class=\"fsk-page-column\">この送信フォームは、JavaScript を有効にすると使えます。</p>";

    private static string ReportBody => Wrap(ReportBodyInner);

    private static string Wrap(string html) => $"<main class=\"{BodyRootClass}\">{html}</main>";

    private static JsonObject Page(string path, string title, string description, string bodyHtml, bool noindex = false)
    {
        var page = new JsonObject
        {
            ["path"] = path,
            ["title"] = title,
            ["description"] = description,
            ["bodyHtml"] = bodyHtml,
        };
        if (noindex) page["noindex"] = true;
        return page;
    }

    private static async Task<string> RenderAsync<T>(SiteContent content, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(content);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            return output.ToHtmlString();
        });
        return Wrap(html);
    }
}
