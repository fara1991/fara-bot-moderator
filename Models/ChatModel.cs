using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// チャット一件分のデータを保持するモデル
/// </summary>
public class ChatModel
{
    /// <summary>
    /// ユーザーのアイコンURL
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    /// <summary>
    /// ユーザー名（表示名）
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// チャットメッセージの内容
    /// </summary>
    [JsonPropertyName("chat")]
    public string Chat { get; set; } = "";
}