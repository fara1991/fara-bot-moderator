using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraBotModeratorSite;
using FaraBotModeratorSite.Build;
using FaraBotModeratorSite.Content;

// 使い方（site/ で）:
//   dotnet run --project src/FaraBotModeratorSite.Build -c Release -- --out <dir> [--content-out <path>]
//   --out          fara-site-kit の build-pages に渡す site.json と pages.json の書き出し先
//   --content-out  ブラウザが起動時に読む data/site.json の書き出し先（既定: src/FaraBotModeratorSite.Web/wwwroot/data/site.json）
// npm run build:site（リポジトリ直下の package.json）が、dotnet publish・build-pages と合わせて実行する。

try
{
    var options = ParseArgs(args);
    var siteDir = FindSiteDir();
    var outDir = Path.GetFullPath(options["out"]);
    var contentOut = Path.GetFullPath(options.GetValueOrDefault("content-out") ?? Path.Combine(siteDir, "src", "FaraBotModeratorSite.Web", "wwwroot", "data", "site.json"));

    // Manual.md → ページ。見出しが無い・pages.json や changelog.json の誤り・リンク切れは、ここで例外になる
    var data = ContentBuilder.LoadFrom(siteDir);
    var content = new SiteContent(data);

    WriteFile(contentOut, SiteJson.Serialize(data));

    var siteJson = JsonNode.Parse(File.ReadAllText(Path.Combine(siteDir, "build", "site.json")))!.AsObject();
    siteJson["notFoundPage"] = StaticPages.NotFoundPage();
    var pagesJson = await StaticPages.BuildPagesJsonAsync(content);
    var pretty = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    WriteFile(Path.Combine(outDir, "site.json"), siteJson.ToJsonString(pretty));
    WriteFile(Path.Combine(outDir, "pages.json"), pagesJson.ToJsonString(pretty));

    Console.WriteLine($"data/site.json: {data.Pages.Count} ページ・更新履歴 {data.Changelog.Count} 件・検索 {data.Search.Count} 件 → {contentOut}");
    Console.WriteLine($"build-pages 用: {pagesJson["pages"]!.AsArray().Count} ページ → {outDir}");
    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or FormatException or IOException or ArgumentException or JsonException)
{
    Console.Error.WriteLine("エラー: " + ex.Message);
    return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            throw new ArgumentException($"引数の形が正しくありません: {args[i]}（--out <dir> [--content-out <path>]）");
        result[args[i][2..]] = args[++i];
    }
    if (!result.ContainsKey("out")) throw new ArgumentException("--out <dir> を指定してください");
    foreach (var key in result.Keys)
        if (key is not ("out" or "content-out")) throw new ArgumentException($"知らない引数です: --{key}");
    return result;
}

/// <summary>site/ のフォルダ（Content/pages.json がある場所）。カレントディレクトリから上へたどる。</summary>
static string FindSiteDir()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Content", "pages.json"))) return dir.FullName;
        var inSite = Path.Combine(dir.FullName, "site");
        if (File.Exists(Path.Combine(inSite, "Content", "pages.json"))) return inSite;
    }
    throw new InvalidOperationException("site/Content/pages.json が見つかりません（リポジトリの中で実行してください）");
}

static void WriteFile(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text, new UTF8Encoding(false));
}
