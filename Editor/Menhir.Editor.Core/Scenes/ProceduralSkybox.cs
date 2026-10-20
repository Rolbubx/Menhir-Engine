using System.Numerics;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Scenes;

public enum SunDisk
{
    None,
    Simple,
    HighQuality,
}

/// <summary>
/// Semi-physical sky (Rayleigh and Mie scattering of the sun's light), with the same settings
/// and defaults as Unity's Skybox/Procedural, the default sky of URP. Its sun follows the
/// scene's first directional light. Scenes without this component use the defaults.
/// The sky also lights the scene (ambient light), scaled by <see cref="AmbientIntensity"/>.
/// </summary>
[ComponentCategory("Rendering")]
public sealed class ProceduralSkybox : Component
{
    public SunDisk SunDisk { get; set; } = SunDisk.HighQuality;

    [Range(0, 1)]
    public float SunSize { get; set; } = 0.04f;

    [Range(1, 10)]
    public float SunSizeConvergence { get; set; } = 5f;

    [Range(0, 5)]
    public float AtmosphereThickness { get; set; } = 1f;

    [Color]
    public Vector4 SkyTint { get; set; } = ColorSpace.GammaToLinear(new Vector4(0.5f, 0.5f, 0.5f, 1f));

    [Color]
    public Vector4 GroundColor { get; set; } = ColorSpace.GammaToLinear(new Vector4(0.369f, 0.349f, 0.341f, 1f));

    [Range(0, 8)]
    public float Exposure { get; set; } = 1.3f;

    [Range(0, 8)]
    [Tooltip("Strength of the ambient light coming from the sky.")]
    public float AmbientIntensity { get; set; } = 1f;
}
