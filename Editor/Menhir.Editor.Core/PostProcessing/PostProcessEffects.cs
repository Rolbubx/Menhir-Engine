using System.Numerics;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.PostProcessing;

// URP's post-processing effects (the overrides of a global Volume), with the same settings
// and defaults. Add them to any entity (the default scene has a "Global Volume" entity);
// when an effect appears more than once in a scene, the first active one is used.
// Colors are linear, like every color in the editor.

/// <summary>Base of the post-processing effects: the checkbox in the component's header turns it off.</summary>
[ComponentCategory("Post-processing")]
public abstract class PostProcessEffect : Component
{
    [HideInInspector]
    public bool Active { get; set; } = true;
}

public enum BloomDownscale
{
    Half,
    Quarter,
}

/// <summary>Glow around bright areas.</summary>
public sealed class Bloom : PostProcessEffect
{
    [Min(0)]
    [Tooltip("Brightness (gamma space) above which pixels glow.")]
    public float Threshold { get; set; } = 0.9f;

    [Min(0)]
    public float Intensity { get; set; }

    [Range(0, 1)]
    [Tooltip("How far the glow spreads.")]
    public float Scatter { get; set; } = 0.7f;

    [Color]
    public Vector4 Tint { get; set; } = Vector4.One;

    [Min(0)]
    [Tooltip("Brightest value that can glow, against fireflies.")]
    public float Clamp { get; set; } = 65472f;

    public bool HighQualityFiltering { get; set; }

    public BloomDownscale Downscale { get; set; } = BloomDownscale.Half;

    [Range(2, 8)]
    public int MaxIterations { get; set; } = 6;
}

/// <summary>Mixes each output channel from the input channels (percentages).</summary>
public sealed class ChannelMixer : PostProcessEffect
{
    [Range(-200, 200)] public float RedOutRedIn { get; set; } = 100f;
    [Range(-200, 200)] public float RedOutGreenIn { get; set; }
    [Range(-200, 200)] public float RedOutBlueIn { get; set; }
    [Range(-200, 200)] public float GreenOutRedIn { get; set; }
    [Range(-200, 200)] public float GreenOutGreenIn { get; set; } = 100f;
    [Range(-200, 200)] public float GreenOutBlueIn { get; set; }
    [Range(-200, 200)] public float BlueOutRedIn { get; set; }
    [Range(-200, 200)] public float BlueOutGreenIn { get; set; }
    [Range(-200, 200)] public float BlueOutBlueIn { get; set; } = 100f;
}

/// <summary>Splits red, green and blue towards the edges of the image, like a cheap lens.</summary>
public sealed class ChromaticAberration : PostProcessEffect
{
    [Range(0, 1)]
    public float Intensity { get; set; }
}

public sealed class ColorAdjustments : PostProcessEffect
{
    [Tooltip("Exposure in EV, applied before tonemapping.")]
    public float PostExposure { get; set; }

    [Range(-100, 100)]
    public float Contrast { get; set; }

    [Color]
    public Vector4 ColorFilter { get; set; } = Vector4.One;

    [Range(-180, 180)]
    public float HueShift { get; set; }

    [Range(-100, 100)]
    public float Saturation { get; set; }
}

/// <summary>Remaps values with curves: master and per channel, then hue and saturation curves.</summary>
public sealed class ColorCurves : PostProcessEffect
{
    public ColorCurve Master { get; set; } = ColorCurve.Identity();
    public ColorCurve Red { get; set; } = ColorCurve.Identity();
    public ColorCurve Green { get; set; } = ColorCurve.Identity();
    public ColorCurve Blue { get; set; } = ColorCurve.Identity();

    [Tooltip("Shifts hues (0.5 = unchanged).")]
    public ColorCurve HueVsHue { get; set; } = ColorCurve.Flat(loop: true);

    [Tooltip("Saturation per hue (0.5 = unchanged).")]
    public ColorCurve HueVsSat { get; set; } = ColorCurve.Flat(loop: true);

    [Tooltip("Saturation per saturation (0.5 = unchanged).")]
    public ColorCurve SatVsSat { get; set; } = ColorCurve.Flat();

    [Tooltip("Saturation per luminance (0.5 = unchanged).")]
    public ColorCurve LumVsSat { get; set; } = ColorCurve.Flat();
}

/// <summary>Grades the image with a lookup texture: a strip of N tiles of N×N (e.g. 1024×32).</summary>
public sealed class ColorLookup : PostProcessEffect
{
    [TextureAsset]
    public Guid? Texture { get; set; }

    [Range(0, 1)]
    public float Contribution { get; set; }
}

public enum DepthOfFieldMode
{
    Off,
    Gaussian,
    Bokeh,
}

/// <summary>Blurs what is out of focus.</summary>
public sealed class DepthOfField : PostProcessEffect
{
    public DepthOfFieldMode Mode { get; set; } = DepthOfFieldMode.Off;

    [Min(0)]
    [Tooltip("Gaussian: distance where the blur starts.")]
    public float GaussianStart { get; set; } = 10f;

    [Min(0)]
    [Tooltip("Gaussian: distance where the blur is at its strongest.")]
    public float GaussianEnd { get; set; } = 30f;

    [Range(0.5f, 1.5f)]
    public float GaussianMaxRadius { get; set; } = 1f;

    public bool HighQualitySampling { get; set; }

    [Min(0.1f)]
    [Tooltip("Bokeh: distance to the point of focus.")]
    public float FocusDistance { get; set; } = 10f;

    [Range(1, 300)]
    [Tooltip("Bokeh: in millimeters.")]
    public float FocalLength { get; set; } = 50f;

    [Range(1, 32)]
    [Tooltip("Bokeh: f-stop; smaller opens the lens and blurs more.")]
    public float Aperture { get; set; } = 5.6f;

    [Range(3, 9)]
    public int BladeCount { get; set; } = 5;

    [Range(0, 1)]
    public float BladeCurvature { get; set; } = 1f;

    [Range(-180, 180)]
    public float BladeRotation { get; set; }
}

public enum FilmGrainType
{
    Thin1,
    Thin2,
    Medium1,
    Medium2,
    Medium3,
    Medium4,
    Medium5,
    Medium6,
    Large01,
    Large02,
}

public sealed class FilmGrain : PostProcessEffect
{
    public FilmGrainType GrainType { get; set; } = FilmGrainType.Thin1;

    [Range(0, 1)]
    public float Intensity { get; set; }

    [Range(0, 1)]
    [Tooltip("Less grain in bright areas as this goes up.")]
    public float Response { get; set; } = 0.8f;
}

public sealed class LensDistortion : PostProcessEffect
{
    [Range(-1, 1)]
    [Tooltip("Positive: barrel distortion; negative: pincushion.")]
    public float Intensity { get; set; }

    [Range(0, 1)]
    public float XMultiplier { get; set; } = 1f;

    [Range(0, 1)]
    public float YMultiplier { get; set; } = 1f;

    public Vector2 Center { get; set; } = new(0.5f, 0.5f);

    [Range(0.01f, 5)]
    public float Scale { get; set; } = 1f;
}

/// <summary>Color wheels for dark (lift), mid (gamma) and bright (gain) tones.</summary>
public sealed class LiftGammaGain : PostProcessEffect
{
    [Trackball] public Vector4 Lift { get; set; } = new(1, 1, 1, 0);
    [Trackball] public Vector4 Gamma { get; set; } = new(1, 1, 1, 0);
    [Trackball] public Vector4 Gain { get; set; } = new(1, 1, 1, 0);
}

public enum MotionBlurQuality
{
    Low,
    Medium,
    High,
}

/// <summary>Blur from the camera's motion (URP's Camera Only mode).</summary>
public sealed class MotionBlur : PostProcessEffect
{
    public MotionBlurQuality Quality { get; set; } = MotionBlurQuality.Low;

    [Range(0, 1)]
    public float Intensity { get; set; }

    [Range(0, 0.2f)]
    [Tooltip("Longest blur, as a fraction of the screen.")]
    public float Clamp { get; set; } = 0.05f;
}

/// <summary>Cylindrical projection that keeps wide fields of view from stretching at the edges.</summary>
public sealed class PaniniProjection : PostProcessEffect
{
    [Range(0, 1)]
    public float Distance { get; set; }

    [Range(0, 1)]
    public float CropToFit { get; set; } = 1f;
}

public sealed class ShadowsMidtonesHighlights : PostProcessEffect
{
    [Trackball] public Vector4 Shadows { get; set; } = new(1, 1, 1, 0);
    [Trackball] public Vector4 Midtones { get; set; } = new(1, 1, 1, 0);
    [Trackball] public Vector4 Highlights { get; set; } = new(1, 1, 1, 0);

    [Min(0)] public float ShadowsStart { get; set; }
    [Min(0)] public float ShadowsEnd { get; set; } = 0.3f;
    [Min(0)] public float HighlightsStart { get; set; } = 0.55f;
    [Min(0)] public float HighlightsEnd { get; set; } = 1f;
}

/// <summary>Tints shadows and highlights separately.</summary>
public sealed class SplitToning : PostProcessEffect
{
    [Color]
    public Vector4 Shadows { get; set; } = ColorSpace.GammaToLinear(new Vector4(0.5f, 0.5f, 0.5f, 1f));

    [Color]
    public Vector4 Highlights { get; set; } = ColorSpace.GammaToLinear(new Vector4(0.5f, 0.5f, 0.5f, 1f));

    [Range(-100, 100)]
    public float Balance { get; set; }
}

public enum TonemappingMode
{
    None,
    Neutral,
    ACES,
}

/// <summary>Maps HDR values to the screen's range.</summary>
public sealed class Tonemapping : PostProcessEffect
{
    public TonemappingMode Mode { get; set; } = TonemappingMode.None;
}

/// <summary>Darkens the edges of the image.</summary>
public sealed class Vignette : PostProcessEffect
{
    [Color]
    public Vector4 Color { get; set; } = new(0, 0, 0, 1);

    public Vector2 Center { get; set; } = new(0.5f, 0.5f);

    [Range(0, 1)]
    public float Intensity { get; set; }

    [Range(0.01f, 1)]
    public float Smoothness { get; set; } = 0.2f;

    [Tooltip("Round instead of following the screen's shape.")]
    public bool Rounded { get; set; }
}

public sealed class WhiteBalance : PostProcessEffect
{
    [Range(-100, 100)]
    [Tooltip("Negative: cooler (blue); positive: warmer (yellow).")]
    public float Temperature { get; set; }

    [Range(-100, 100)]
    [Tooltip("Negative: green; positive: magenta.")]
    public float Tint { get; set; }
}
