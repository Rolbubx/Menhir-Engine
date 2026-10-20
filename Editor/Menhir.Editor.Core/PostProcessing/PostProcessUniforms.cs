using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.PostProcessing;

public enum UniformKind
{
    Int,
    Float,
    Vec2,
    Vec3,
    Vec4,
}

/// <summary>A shader uniform value; ints are stored in X.</summary>
public readonly record struct ShaderUniform(string Name, UniformKind Kind, Vector4 Value)
{
    public static ShaderUniform Int(string name, int value) => new(name, UniformKind.Int, new Vector4(value, 0, 0, 0));
    public static ShaderUniform Float(string name, float value) => new(name, UniformKind.Float, new Vector4(value, 0, 0, 0));
    public static ShaderUniform Vec2(string name, Vector2 value) => new(name, UniformKind.Vec2, new Vector4(value, 0, 0));
    public static ShaderUniform Vec3(string name, Vector3 value) => new(name, UniformKind.Vec3, new Vector4(value, 0));
    public static ShaderUniform Vec4(string name, Vector4 value) => new(name, UniformKind.Vec4, value);
}

/// <summary>
/// The uniforms of the uber pass that only depend on the effects' settings (not on the image size
/// or the frame): lens distortion, chromatic aberration and color grading. The editor sets them
/// every frame; the game build stores them in its data file.
/// </summary>
public static class PostProcessUniforms
{
    /// <summary>Samples per row of the curve texture (8 rows, see <see cref="BakeCurves"/>).</summary>
    public const int CurveSamples = 128;

    public static List<ShaderUniform> Uber(PostProcessStack stack)
    {
        var u = new List<ShaderUniform>();

        bool distortion = stack.LensDistortion is { } ld && (ld.Intensity != 0f || ld.Scale != 1f);
        u.Add(ShaderUniform.Int("uDistortionOn", distortion ? 1 : 0));
        if (distortion)
        {
            var (center, parameters) = PostProcessMath.LensDistortion(stack.LensDistortion!);
            u.Add(ShaderUniform.Vec4("uDistortionCenter", center));
            u.Add(ShaderUniform.Vec4("uDistortionParams", parameters));
        }

        u.Add(ShaderUniform.Float("uChromaAmount", (stack.ChromaticAberration?.Intensity ?? 0f) * 0.05f));

        ColorAdjustments? adjustments = stack.ColorAdjustments;
        u.Add(ShaderUniform.Float("uPostExposure", MathF.Pow(2f, adjustments?.PostExposure ?? 0f)));
        u.Add(ShaderUniform.Vec3("uHueSatCon", new Vector3(
            (adjustments?.HueShift ?? 0f) / 360f,
            (adjustments?.Saturation ?? 0f) / 100f + 1f,
            (adjustments?.Contrast ?? 0f) / 100f + 1f)));
        u.Add(ShaderUniform.Vec3("uColorFilter", adjustments != null ? Xyz(adjustments.ColorFilter) : Vector3.One));

        WhiteBalance? whiteBalance = stack.WhiteBalance is { } wb && (wb.Temperature != 0f || wb.Tint != 0f) ? wb : null;
        u.Add(ShaderUniform.Int("uWhiteBalanceOn", whiteBalance != null ? 1 : 0));
        if (whiteBalance != null)
            u.Add(ShaderUniform.Vec3("uWhiteBalance",
                PostProcessMath.WhiteBalanceCoefficients(whiteBalance.Temperature, whiteBalance.Tint)));

        SplitToning? split = stack.SplitToning;
        u.Add(ShaderUniform.Int("uSplitToningOn", split != null ? 1 : 0));
        if (split != null)
        {
            Vector4 shadows = ColorSpace.LinearToGamma(split.Shadows);
            Vector4 highlights = ColorSpace.LinearToGamma(split.Highlights);
            u.Add(ShaderUniform.Vec4("uSplitShadows", new Vector4(shadows.X, shadows.Y, shadows.Z, split.Balance / 100f)));
            u.Add(ShaderUniform.Vec3("uSplitHighlights", Xyz(highlights)));
        }

        ChannelMixer? mixer = stack.ChannelMixer;
        u.Add(ShaderUniform.Int("uChannelMixerOn", mixer != null ? 1 : 0));
        if (mixer != null)
        {
            u.Add(ShaderUniform.Vec3("uChannelMixerRed",
                new Vector3(mixer.RedOutRedIn, mixer.RedOutGreenIn, mixer.RedOutBlueIn) / 100f));
            u.Add(ShaderUniform.Vec3("uChannelMixerGreen",
                new Vector3(mixer.GreenOutRedIn, mixer.GreenOutGreenIn, mixer.GreenOutBlueIn) / 100f));
            u.Add(ShaderUniform.Vec3("uChannelMixerBlue",
                new Vector3(mixer.BlueOutRedIn, mixer.BlueOutGreenIn, mixer.BlueOutBlueIn) / 100f));
        }

        ShadowsMidtonesHighlights? smh = stack.ShadowsMidtonesHighlights;
        u.Add(ShaderUniform.Int("uSmhOn", smh != null ? 1 : 0));
        if (smh != null)
        {
            var (s, m, h) = PostProcessMath.ShadowsMidtonesHighlights(smh.Shadows, smh.Midtones, smh.Highlights);
            u.Add(ShaderUniform.Vec3("uSmhShadows", s));
            u.Add(ShaderUniform.Vec3("uSmhMidtones", m));
            u.Add(ShaderUniform.Vec3("uSmhHighlights", h));
            u.Add(ShaderUniform.Vec4("uSmhLimits",
                new Vector4(smh.ShadowsStart, smh.ShadowsEnd, smh.HighlightsStart, smh.HighlightsEnd)));
        }

        LiftGammaGain? lgg = stack.LiftGammaGain;
        u.Add(ShaderUniform.Int("uLiftGammaGainOn", lgg != null ? 1 : 0));
        if (lgg != null)
        {
            var (lift, gamma, gain) = PostProcessMath.LiftGammaGain(lgg.Lift, lgg.Gamma, lgg.Gain);
            u.Add(ShaderUniform.Vec3("uLift", lift));
            u.Add(ShaderUniform.Vec3("uGamma", gamma));
            u.Add(ShaderUniform.Vec3("uGain", gain));
        }

        u.Add(ShaderUniform.Int("uCurvesOn", stack.ColorCurves != null ? 1 : 0));

        u.Add(ShaderUniform.Int("uTonemapping", stack.Tonemapping?.Mode switch
        {
            TonemappingMode.Neutral => 1,
            TonemappingMode.ACES => 2,
            _ => 0,
        }));
        return u;
    }

    /// <summary>
    /// The 8 curves of Color Curves (master, red, green, blue, hue/hue, hue/sat, sat/sat, lum/sat),
    /// <see cref="CurveSamples"/> values each, row after row: the curve texture's pixels.
    /// </summary>
    public static float[] BakeCurves(ColorCurves curves)
    {
        ColorCurve[] rows =
            [curves.Master, curves.Red, curves.Green, curves.Blue, curves.HueVsHue, curves.HueVsSat, curves.SatVsSat, curves.LumVsSat];
        var values = new float[CurveSamples * rows.Length];
        for (int row = 0; row < rows.Length; row++)
            rows[row].Bake(CurveSamples).CopyTo(values, row * CurveSamples);
        return values;
    }

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);
}
