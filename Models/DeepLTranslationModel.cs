using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class DeepLTranslationModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("translations")]
    public List<DeepLTranslationChildModel> Translations { get; set; } = new();
}

/// <summary>
/// </summary>
public abstract class DeepLTranslationChildModel
{
    /// <summary>
    /// </summary>
    /// <param name="language"></param>
    /// <param name="text"></param>
    protected DeepLTranslationChildModel(string language, string text)
    {
        Language = language;
        Text = text;
    }

    /// <summary>
    /// </summary>
    [JsonPropertyName("detected_source_language")]
    public string Language { get; } = "ja";

    /// <summary>
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; } = "";
}