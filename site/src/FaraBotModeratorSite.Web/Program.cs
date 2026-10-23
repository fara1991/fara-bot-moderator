using FaraBotModeratorSite;
using FaraBotModeratorSite.Search;
using FaraBotModeratorSite.Web;
using FaraBotModeratorSite.Web.Layout;
using FaraBotModeratorSite.Web.Services;
using FaraSiteKit.Core;
using FaraSiteKit.Razor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = builder.HostEnvironment.BaseAddress;

// 共通部品（fara-site-kit の Razor。ヘッダー・フッター・テーマ・報告フォームなど）の設定・テーマの状態・MudBlazor（通知の位置など）。
// メンテナンス画面（下）も SiteOptions を読むので、メンテナンスの分岐より前に登録する。値は SiteInfo（Shared）
builder.Services.AddFaraSiteKit(SiteConfig.Configure);

// 報告フォーム（kit の ReportFormView）が使う。メンテナンスの状態・説明ページの中身の取得にも使う
var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
builder.Services.AddSingleton(http);

// メンテナンスの状態（Worker の変数 MAINTENANCE。external/fara-site-kit/worker/maintenance.js）。読めなければ通常。
// メンテナンス中（MAINTENANCE=true）は、アプリの代わりに、メンテナンス画面だけを動かす
var maintenance = await MaintenanceInfo.LoadAsync(http);
builder.Services.AddSingleton(maintenance);
if (maintenance.State == MaintenanceState.On)
{
    builder.RootComponents.Add<MaintenancePage>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
    await builder.Build().RunAsync();
    return;
}

// サイトの中身（説明ページ・更新履歴・検索の索引）。ビルドが Manual.md から作った data/site.json を、起動時に 1 回だけ読む
using (var stream = await http.GetStreamAsync("data/site.json"))
{
    var content = new SiteContent(await SiteJson.DeserializeAsync(stream));
    builder.Services.AddSingleton(content);
    builder.Services.AddSingleton(new SearchIndex(content.Search));
}

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");   // 報告・404 の noindex と、ページの題名に必要

await builder.Build().RunAsync();
