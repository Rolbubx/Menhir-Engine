using System.Numerics;
using Menhir.Editor.PostProcessing;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Lighting;

/// <summary>A point or spot light, in world space, ready for the shaders.</summary>
/// <param name="Direction">Where a spot light points (unused for point lights).</param>
/// <param name="Color">Linear color times intensity.</param>
/// <param name="SpotCosOuter">Cosine of half the cone angle; -1 for point lights.</param>
/// <param name="SpotCosInner">Where the cone starts to fade.</param>
public readonly record struct PunctualLight(Vector3 Position, Vector3 Direction, Vector3 Color, float Range,
    float SpotCosOuter, float SpotCosInner, LightShadows Shadows = LightShadows.None, float ShadowStrength = 1f)
{
    public bool IsSpot => SpotCosOuter > -1f;
}

/// <summary>The active post-processing effects of a scene (the first active one of each kind).</summary>
public sealed class PostProcessStack
{
    public Bloom? Bloom { get; init; }
    public ChannelMixer? ChannelMixer { get; init; }
    public ChromaticAberration? ChromaticAberration { get; init; }
    public ColorAdjustments? ColorAdjustments { get; init; }
    public ColorCurves? ColorCurves { get; init; }
    public ColorLookup? ColorLookup { get; init; }
    public DepthOfField? DepthOfField { get; init; }
    public FilmGrain? FilmGrain { get; init; }
    public LensDistortion? LensDistortion { get; init; }
    public LiftGammaGain? LiftGammaGain { get; init; }
    public MotionBlur? MotionBlur { get; init; }
    public PaniniProjection? PaniniProjection { get; init; }
    public ShadowsMidtonesHighlights? ShadowsMidtonesHighlights { get; init; }
    public SplitToning? SplitToning { get; init; }
    public Tonemapping? Tonemapping { get; init; }
    public Vignette? Vignette { get; init; }
    public WhiteBalance? WhiteBalance { get; init; }

    public static PostProcessStack Empty { get; } = new();
}

/// <summary>
/// Everything a scene contributes to lighting and the final image: the sun, other lights,
/// the sky and its ambient light, and the post-processing effects.
/// </summary>
public sealed class SceneEnvironment
{
    /// <summary>URP draws up to 8 additional lights per object; the extra ones are left out.</summary>
    public const int MaxAdditionalLights = 8;

    public required ProceduralSkybox Sky { get; init; }

    /// <summary>Points towards the sun (the first directional light, or a default direction).</summary>
    public required Vector3 ToSun { get; init; }

    /// <summary>Linear color times intensity of the sun; zero when the scene has no directional light.</summary>
    public required Vector3 SunColor { get; init; }

    public LightShadows SunShadows { get; init; }

    public float SunShadowStrength { get; init; } = 1f;

    public required Vector3 AmbientTop { get; init; }
    public required Vector3 AmbientHorizon { get; init; }
    public required Vector3 AmbientBottom { get; init; }

    public required IReadOnlyList<PunctualLight> AdditionalLights { get; init; }

    public required PostProcessStack PostProcessing { get; init; }

    public static SceneEnvironment Gather(Scene scene)
    {
        ProceduralSkybox sky = Find<ProceduralSkybox>(scene) ?? new ProceduralSkybox();

        Vector3 toSun = SkyModel.DefaultToSun;
        Vector3 sunColor = Vector3.Zero;
        LightShadows sunShadows = LightShadows.None;
        float sunShadowStrength = 1f;
        var additional = new List<PunctualLight>();
        bool hasSun = false;

        foreach (Entity entity in scene.Entities)
        {
            if (entity.GetComponent<Light>() is not { } light)
                continue;
            Matrix4x4 world = scene.GetWorldMatrix(entity);
            Vector3 forward = Forward(world);
            Vector3 color = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity;

            if (light.LightType == LightType.Directional)
            {
                if (hasSun)
                    continue; // URP lights with one main directional light
                hasSun = true;
                toSun = -forward;
                sunColor = color;
                sunShadows = light.Shadows;
                sunShadowStrength = light.ShadowStrength;
            }
            else if (additional.Count < MaxAdditionalLights)
            {
                float outer = light.LightType == LightType.Spot ? Math.Clamp(light.SpotAngle, 1f, 179f) * MathF.PI / 360f : MathF.PI;
                additional.Add(new PunctualLight(world.Translation, forward, color, MathF.Max(light.Range, 0.01f),
                    light.LightType == LightType.Spot ? MathF.Cos(outer) : -1f,
                    light.LightType == LightType.Spot ? MathF.Cos(outer * 0.8f) : -1f,
                    light.Shadows, light.ShadowStrength));
            }
        }

        (Vector3 top, Vector3 horizon, Vector3 bottom) = SkyModel.Ambient(sky, toSun);
        return new SceneEnvironment
        {
            Sky = sky,
            ToSun = toSun,
            SunColor = sunColor,
            SunShadows = sunShadows,
            SunShadowStrength = sunShadowStrength,
            AmbientTop = top,
            AmbientHorizon = horizon,
            AmbientBottom = bottom,
            AdditionalLights = additional,
            PostProcessing = GatherPostProcessing(scene),
        };
    }

    private static PostProcessStack GatherPostProcessing(Scene scene) => new()
    {
        Bloom = FindEffect<Bloom>(scene),
        ChannelMixer = FindEffect<ChannelMixer>(scene),
        ChromaticAberration = FindEffect<ChromaticAberration>(scene),
        ColorAdjustments = FindEffect<ColorAdjustments>(scene),
        ColorCurves = FindEffect<ColorCurves>(scene),
        ColorLookup = FindEffect<ColorLookup>(scene),
        DepthOfField = FindEffect<DepthOfField>(scene),
        FilmGrain = FindEffect<FilmGrain>(scene),
        LensDistortion = FindEffect<LensDistortion>(scene),
        LiftGammaGain = FindEffect<LiftGammaGain>(scene),
        MotionBlur = FindEffect<MotionBlur>(scene),
        PaniniProjection = FindEffect<PaniniProjection>(scene),
        ShadowsMidtonesHighlights = FindEffect<ShadowsMidtonesHighlights>(scene),
        SplitToning = FindEffect<SplitToning>(scene),
        Tonemapping = FindEffect<Tonemapping>(scene),
        Vignette = FindEffect<Vignette>(scene),
        WhiteBalance = FindEffect<WhiteBalance>(scene),
    };

    /// <summary>The entity's forward axis (-Z) in world space.</summary>
    public static Vector3 Forward(Matrix4x4 world)
    {
        Vector3 forward = Vector3.TransformNormal(-Vector3.UnitZ, world);
        return forward.LengthSquared() > 1e-12f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
    }

    private static T? Find<T>(Scene scene) where T : Component =>
        scene.Entities.Select(e => e.GetComponent<T>()).FirstOrDefault(c => c != null);

    private static T? FindEffect<T>(Scene scene) where T : PostProcessEffect =>
        scene.Entities.SelectMany(e => e.Components.OfType<T>()).FirstOrDefault(c => c.Active);
}
