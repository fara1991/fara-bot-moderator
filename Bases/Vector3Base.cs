using System.Text.Json.Serialization;

namespace FaraBotModerator.Bases;

/// <summary>
/// </summary>
public class Vector3Base
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("x")]
    public int x { get; set; } = 0;

    /// <summary>
    /// </summary>
    [JsonPropertyName("y")]
    public int y { get; set; } = 0;

    /// <summary>
    /// </summary>
    [JsonPropertyName("z")]
    public int z { get; set; } = 0;
}