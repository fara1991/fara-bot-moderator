using System.Text.Json.Serialization;
using FaraBotModerator.Bases;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class SaberTailorModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("ConfigVersion")]
    public int ConfigVersion { get; set; } = 5;

    /// <summary>
    /// </summary>
    [JsonPropertyName("IsSaberScaleModEnabled")]
    public bool IsSaberScaleModEnabled { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberScaleHitbox")]
    public bool SaberScaleHitbox { get; set; } = false;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberLength")]
    public int SaberLength { get; set; } = 100;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberGirth")]
    public int SaberGirth { get; set; } = 100;

    /// <summary>
    /// </summary>
    [JsonPropertyName("IsTrailModEnabled")]
    public bool IsTrailModEnabled { get; set; } = false;

    /// <summary>
    /// </summary>
    [JsonPropertyName("IsTrailEnabled")]
    public bool IsTrailEnabled { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("TrailDuration")]
    public int TrailDuration { get; set; } = 300;

    /// <summary>
    /// </summary>
    [JsonPropertyName("TrailGranularity")]
    public int TrailGranularity { get; set; } = 60;

    /// <summary>
    /// </summary>
    [JsonPropertyName("TrailWhiteSectionDuration")]
    public int TrailWhiteSectionDuration { get; set; } = 100;

    /// <summary>
    /// </summary>
    [JsonPropertyName("IsGripModEnabled")]
    public bool IsGripModEnabled { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripLeftPosition")]
    public GripLeftPositionModel GripLeftPosition { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripRightPosition")]
    public GripRightPositionModel GripRightPosition { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripLeftRotation")]
    public GripLeftRotationModel GripLeftRotation { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripRightRotation")]
    public GripRightRotationModel GripRightRotation { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripLeftOffset")]
    public GripLeftOffsetModel GripLeftOffset { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("GripRightOffset")]
    public GripRightOffsetModel GripRightOffset { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("ModifyMenuHiltGrip")]
    public bool ModifyMenuHiltGrip { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("UseBaseGameAdjustmentMode")]
    public bool UseBaseGameAdjustmentMode { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberPosIncrement")]
    public int SaberPosIncrement { get; set; } = 5;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberPosIncValue")]
    public int SaberPosIncValue { get; set; } = 5;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberRotIncrement")]
    public int SaberRotIncrement { get; set; } = 1;

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberPosIncUnit")]
    public string SaberPosIncUnit { get; set; } = "mm";

    /// <summary>
    /// </summary>
    [JsonPropertyName("SaberPosDisplayUnit")]
    public string SaberPosDisplayUnit { get; set; } = "cm";
}

/// <summary>
/// </summary>
public class GripLeftPositionModel : Vector3Base
{
}

/// <summary>
/// </summary>
public class GripRightPositionModel : Vector3Base
{
}

/// <summary>
/// </summary>
public class GripLeftRotationModel : Vector3Base
{
}

/// <summary>
/// </summary>
public class GripRightRotationModel : Vector3Base
{
}

/// <summary>
/// </summary>
public class GripLeftOffsetModel : Vector3Base
{
}

/// <summary>
/// </summary>
public class GripRightOffsetModel : Vector3Base
{
}