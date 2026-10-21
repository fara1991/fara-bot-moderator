using FaraBotModeratorSite.Components;
using FaraBotModeratorSite.Data;
using FaraBotModeratorSite.Publishing;

var builder = WebApplication.CreateBuilder(args);

// 閲覧専用サイトなので静的 SSR のみ（SignalR 接続なし）。
builder.Services.AddRazorComponents();
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.AddSingleton<ContentService>();

// dotnet run -- --export <dir> で静的サイトとして書き出す（Cloudflare へのデプロイ用）
var exportDir = builder.Configuration["export"];
var exporting = !string.IsNullOrEmpty(exportDir);
if (exporting)
{
    // 書き出し中は自分自身に HTTP でアクセスするので、空いているポートで HTTP のみ待ち受ける
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/not-found", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    // 開発中は Manual.md と pages.json を編集したら、リロードするだけで反映する
    app.Use(async (context, next) =>
    {
        context.RequestServices.GetRequiredService<ContentService>().Reload();
        await next(context);
    });
}

// 未定義の URL は空の 404 ではなく /not-found ページを 404 ステータスで返す
app.UseStatusCodePagesWithReExecute("/not-found");

if (!exporting)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();

// 検索インデックス（クライアント側の検索 wwwroot/js/search.js が読み込む。静的に書き出すときも、同じ内容を /search-index.json に書く）
var searchJsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
{
    // 日本語を \uXXXX にエスケープしない
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
app.MapGet("/search-index.json", (ContentService content) => Results.Json(content.BuildSearchIndex(), searchJsonOptions));

app.MapRazorComponents<App>();

if (exporting)
{
    await app.StartAsync();
    await StaticSiteExporter.ExportAsync(app, exportDir!);
    await app.StopAsync();
    return;
}

app.Run();
