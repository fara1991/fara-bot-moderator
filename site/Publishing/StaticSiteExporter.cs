using FaraBotModeratorSite.Data;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FaraBotModeratorSite.Publishing;

/// <summary>
/// 起動中のアプリから全ページを取得し、静的サイト（HTML + 静的ファイル）として書き出す。
/// Cloudflare Workers (Static Assets) にそのままデプロイできる形にする。
/// 使い方: dotnet run -- --export dist
/// </summary>
public static class StaticSiteExporter
{
    private static readonly System.Text.RegularExpressions.Regex DeadAnchor = new("href=\"#(?!main-content\")[^\"]*\"");

    private const string MarkerFile = ".site-export";

    public static async Task ExportAsync(WebApplication app, string outputDir)
    {
        var content = app.Services.GetRequiredService<ContentService>();
        var baseAddress = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };

        outputDir = Path.GetFullPath(outputDir);
        // 出力先は丸ごと消すので、プロジェクトのフォルダやその親（--export . など）は受け付けない
        var projectDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(app.Environment.ContentRootPath));
        var outputTrimmed = Path.TrimEndingDirectorySeparator(outputDir);
        if (string.Equals(projectDir, outputTrimmed, StringComparison.OrdinalIgnoreCase) ||
            projectDir.StartsWith(outputTrimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"--export には専用の出力フォルダ（例: dist）を指定してください: {outputDir}");
        }
        // 書き出し先は丸ごと消す。空か、前回の書き出しの印（.site-export）があるフォルダだけを対象にする
        // （--export wwwroot や --export Content のような、ソースのフォルダを消さないため）
        var marker = Path.Combine(outputDir, MarkerFile);
        if (Directory.Exists(outputDir))
        {
            if (Directory.EnumerateFileSystemEntries(outputDir).Any() && !File.Exists(marker))
                throw new InvalidOperationException($"--export の出力先は、空のフォルダか、前回の書き出し先（dist など）にしてください: {outputDir}");
            Directory.Delete(outputDir, recursive: true);
        }
        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(marker, "このフォルダは StaticSiteExporter が書き出したものです（次の書き出しで丸ごと消します）。");

        // Workers の html_handling = "auto-trailing-slash" に合わせ、/docs/overview は docs/overview.html に書く。
        var pages = new Dictionary<string, string>
        {
            ["/"] = "index.html",
            ["/docs"] = "docs.html",
            ["/search-index.json"] = "search-index.json",
        };
        foreach (var page in content.Pages) pages[page.Url] = page.Url.TrimStart('/') + ".html";

        foreach (var (path, file) in pages)
        {
            using var response = await http.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}");
            }
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // 説明ページに、行き先のないページ内リンク（#見出し）が残っていたら、リンク切れなので止める（スキップリンク以外）
            if (file.EndsWith(".html", StringComparison.Ordinal) && DeadAnchor.Match(System.Text.Encoding.UTF8.GetString(bytes)) is { Success: true } dead)
                throw new InvalidOperationException($"{path} に、行き先のないリンクがあります: {dead.Value}（Manual.md の見出しの名前と pages.json を確認してください）");
            await WriteAsync(outputDir, file, bytes);
        }

        // 404 ページ（Workers の not_found_handling = "404-page" が /404.html を返す）
        using (var notFound = await http.GetAsync("/__static_export_404__"))
        {
            if (notFound.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Expected 404 for the not-found page, got {(int)notFound.StatusCode}");
            }
            await WriteAsync(outputDir, "404.html", await notFound.Content.ReadAsByteArrayAsync());
        }

        CopyDirectory(app.Environment.WebRootPath, outputDir);

        var fileCount = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories).Count();
        app.Logger.LogInformation("Exported {Pages} pages, {Files} files in total to {Dir}", pages.Count + 1, fileCount, outputDir);
    }

    private static async Task WriteAsync(string outputDir, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(outputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
