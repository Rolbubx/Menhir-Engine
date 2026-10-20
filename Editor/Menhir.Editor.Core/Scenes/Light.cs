using System.Numerics;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Scenes;

public enum LightType
{
    Directional,
    Point,
    Spot,
}

public enum LightShadows
{
    None,
    Hard,
    Soft,
}

/// <summary>
/// A light, shining along the entity's forward axis (-Z) for directional and spot lights.
/// The first directional light is the sun: it also positions the sun of the procedural skybox.
/// </summary>
[ComponentCategory("Rendering")]
public sealed class Light : Component
{
    public LightType LightType { get; set; } = LightType.Directional;

    /// <summary>Linear RGB; Unity's default warm white.</summary>
    [Color]
    public Vector4 Color { get; set; } = ColorSpace.GammaToLinear(new Vector4(1f, 0.9568627f, 0.8392157f, 1f));

    [Min(0)]
    public float Intensity { get; set; } = 1f;

    [Min(0)]
    [Tooltip("Point and spot lights: distance at which the light fades out.")]
    public float Range { get; set; } = 10f;

    [Range(1, 179)]
    [Tooltip("Spot lights: angle of the cone, in degrees.")]
    public float SpotAngle { get; set; } = 30f;

    [Tooltip("Soft shadows have filtered edges (if the project allows them, see Project Settings).")]
    public LightShadows Shadows { get; set; } = LightShadows.None;

    [Range(0, 1)]
    public float ShadowStrength { get; set; } = 1f;
}
