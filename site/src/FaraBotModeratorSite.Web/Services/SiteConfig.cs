using FaraSiteKit.Core;

namespace FaraBotModeratorSite.Web.Services;

/// <summary>共通部品（fara-site-kit の Razor）の設定に、このサイトの値（SiteInfo）を入れる。</summary>
public static class SiteConfig
{
    public static void Configure(SiteOptions options)
    {
        options.Name = SiteInfo.Name;
        options.Author = SiteInfo.Author;
        options.PublishedYear = SiteInfo.PublishedYear;
        options.AuthorHomeUrl = SiteInfo.PortfolioUrl;
        options.HomeHref = "/";
        // 以降の相対パスは <base href="/"> からの相対（ReportPath・PrivacyPath は、フッターのリンクになる）
        options.ReportPath = "report";
        options.PrivacyPath = "privacy";
        options.DevelopHost = SiteInfo.DevelopHost;
        options.StorageKey = SiteInfo.StorageKey;
        options.SidebarKey = SiteInfo.SidebarKey;
        foreach (var (service, url) in SiteInfo.Social) options.Social.Add(new SiteSocialLink(service, url));
    }
}
