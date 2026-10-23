using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaraBotModeratorSite;

/// <summary>/data/site.json の読み書き。ブラウザ（トリミングされる WebAssembly）でも動くよう、ソース生成のシリアライザーを使う。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SiteContentData))]
[JsonSerializable(typeof(List<ChangelogEntry>))]
internal sealed partial class SiteJsonContext : JsonSerializerContext
{
}

public static class SiteJson
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        // 日本語を \uXXXX にエスケープしない
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = SiteJsonContext.Default,
    };

    public static string Serialize(SiteContentData data) => JsonSerializer.Serialize(data, WriteOptions);

    public static SiteContentData Deserialize(string json) =>
        JsonSerializer.Deserialize(json, SiteJsonContext.Default.SiteContentData) ?? new SiteContentData();

    /// <summary>changelog.json（[ { "date": "2026-10-22", "title": "...", "items": ["..."] } ]）を読む。</summary>
    public static List<ChangelogEntry> DeserializeChangelog(string json) =>
        JsonSerializer.Deserialize(json, SiteJsonContext.Default.ListChangelogEntry) ?? new();
}
