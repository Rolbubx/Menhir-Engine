using Menhir.Editor.Inspection;

namespace Menhir.Editor.Projects;

public enum ShadowMapSize
{
    Size256 = 256,
    Size512 = 512,
    Size1024 = 1024,
    Size2048 = 2048,
    Size4096 = 4096,
}

public enum SoftShadowQuality
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Settings of the Menhir Universal Pipeline, like URP's pipeline asset (shadows for now).
/// Saved in ProjectSettings.json; edited in Edit > Project Settings.
/// </summary>
public sealed class RenderingSettings
{
    public bool MainLightShadows { get; set; } = true;

    [Tooltip("Size of the main light's shadow map; cascades share it.")]
    public ShadowMapSize MainLightShadowResolution { get; set; } = ShadowMapSize.Size2048;

    public bool AdditionalLightShadows { get; set; } = true;

    [Tooltip("Size of the shadow map shared by point and spot lights (a point light takes 6 tiles).")]
    public ShadowMapSize AdditionalLightShadowResolution { get; set; } = ShadowMapSize.Size2048;

    [Min(0.1f)]
    [Tooltip("Main light shadows end (fading out) this far from the camera.")]
    public float MaxDistance { get; set; } = 50f;

    [Range(1, 4)]
    public int CascadeCount { get; set; } = 4;

    [Range(0, 1)]
    [Tooltip("2 cascades: where the first one ends, as a fraction of Max Distance.")]
    public float Cascade2Split { get; set; } = 0.25f;

    [Range(0, 1)] public float Cascade3Split1 { get; set; } = 0.1f;
    [Range(0, 1)] public float Cascade3Split2 { get; set; } = 0.3f;
    [Range(0, 1)] public float Cascade4Split1 { get; set; } = 0.067f;
    [Range(0, 1)] public float Cascade4Split2 { get; set; } = 0.2f;
    [Range(0, 1)] public float Cascade4Split3 { get; set; } = 0.467f;

    [Range(0, 1)]
    [Tooltip("Part of Max Distance over which shadows fade out.")]
    public float LastBorder { get; set; } = 0.2f;

    [Range(0, 10)]
    [Tooltip("Pushes shadows away from the light, in shadow map texels (against shadow acne).")]
    public float DepthBias { get; set; } = 1f;

    [Range(0, 10)]
    [Tooltip("Shrinks shadow casters along their normals, in shadow map texels.")]
    public float NormalBias { get; set; } = 1f;

    [Tooltip("Lets lights use Soft shadows (filtered edges); otherwise they are hard.")]
    public bool SoftShadows { get; set; } = true;

    public SoftShadowQuality SoftShadowQuality { get; set; } = SoftShadowQuality.Medium;

    /// <summary>Where each cascade ends, as fractions of <see cref="MaxDistance"/> (the last is 1).</summary>
    public float[] CascadeEnds()
    {
        float[] ends = Math.Clamp(CascadeCount, 1, 4) switch
        {
            1 => [1f],
            2 => [Cascade2Split, 1f],
            3 => [Cascade3Split1, Cascade3Split2, 1f],
            _ => [Cascade4Split1, Cascade4Split2, Cascade4Split3, 1f],
        };
        // Keep them increasing even if the splits were set out of order.
        for (int i = 1; i < ends.Length; i++)
            ends[i] = MathF.Max(ends[i], ends[i - 1] + 0.001f);
        return ends;
    }
}
