using System.Text.Json.Serialization;

namespace FaraBotModerator.Bases;

/// <summary>
/// </summary>
public class Vector3Base
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("x")]
    public int x { get; set; }

    /// <summary>
    /// </summary>
    [JsonPropertyName("y")]
    public int y { get; set; }

    /// <summary>
    /// </summary>
    [JsonPropertyName("z")]
    public int z { get; set; }
}