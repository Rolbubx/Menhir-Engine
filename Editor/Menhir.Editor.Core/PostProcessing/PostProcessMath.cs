using System.Numerics;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.PostProcessing;

/// <summary>
/// CPU side of the effects: turns the settings of the components into the values the
/// shaders use, following URP (ColorUtils and PostProcessPass).
/// </summary>
public static class PostProcessMath
{
    /// <summary>Rec. 709 luminance of a linear color.</summary>
    public static float Luminance(Vector3 c) => c.X * 0.2126729f + c.Y * 0.7151522f + c.Z * 0.0721750f;

    /// <summary>
    /// Per-channel multipliers in LMS space for White Balance: the D65 white point moved
    /// along the daylight locus by <paramref name="temperature"/> and across it by <paramref name="tint"/>.
    /// (0, 0) gives about (1, 1, 1).
    /// </summary>
    public static Vector3 WhiteBalanceCoefficients(float temperature, float tint)
    {
        float t1 = temperature / 65f;
        float t2 = tint / 65f;

        float x = 0.31271f - t1 * (t1 < 0f ? 0.1f : 0.05f);
        float y = StandardIlluminantY(x) + t2 * 0.05f;

        var d65 = new Vector3(0.949237f, 1.03542f, 1.08728f);
        Vector3 white = CieXyToLms(x, y);
        return d65 / white;
    }

    private static float StandardIlluminantY(float x) => 2.87f * x - 3f * x * x - 0.27509507f;

    private static Vector3 CieXyToLms(float x, float y)
    {
        float bigY = 1f;
        float bigX = bigY * x / y;
        float bigZ = bigY * (1f - x - y) / y;
        return new Vector3(
            0.7328f * bigX + 0.4296f * bigY - 0.1624f * bigZ,
            -0.7036f * bigX + 1.6975f * bigY + 0.0061f * bigZ,
            0.0030f * bigX + 0.0136f * bigY + 0.9834f * bigZ);
    }

    /// <summary>
    /// Lift (added), gamma (exponent) and gain (multiplier) per channel. The color of each wheel only
    /// tints (its luminance is removed) and w moves all channels. Neutral wheels give (0, 1, 1).
    /// </summary>
    public static (Vector3 Lift, Vector3 Gamma, Vector3 Gain) LiftGammaGain(Vector4 lift, Vector4 gamma, Vector4 gain)
    {
        // Unity works on the wheel colors as picked (gamma); the components store linear colors.
        Vector3 Tint(Vector4 wheel) => Xyz(ColorSpace.LinearToGamma(wheel));

        Vector3 l = Tint(lift);
        Vector3 outLift = l - new Vector3(Luminance(l)) + new Vector3(lift.W);

        Vector3 g = Tint(gamma);
        Vector3 outGamma = g - new Vector3(Luminance(g)) + new Vector3(gamma.W + 1f);
        outGamma = Vector3.One / Vector3.Max(outGamma, new Vector3(1e-3f));

        Vector3 n = Tint(gain);
        Vector3 outGain = n - new Vector3(Luminance(n)) + new Vector3(gain.W + 1f);
        return (outLift, outGamma, outGain);
    }

    /// <summary>Multipliers for shadows, midtones and highlights (neutral wheels give 1).</summary>
    public static (Vector3 Shadows, Vector3 Midtones, Vector3 Highlights) ShadowsMidtonesHighlights(
        Vector4 shadows, Vector4 midtones, Vector4 highlights)
    {
        static Vector3 Prepare(Vector4 wheel)
        {
            float weight = wheel.W * (wheel.W < 0f ? 1f : 4f);
            return Vector3.Max(Xyz(wheel) + new Vector3(weight), Vector3.Zero);
        }
        return (Prepare(shadows), Prepare(midtones), Prepare(highlights));
    }

    /// <summary>Bloom tint normalized to a luminance of 1, so it changes the hue and not the strength.</summary>
    public static Vector3 BloomTint(Vector4 tint)
    {
        Vector3 c = Xyz(tint);
        float luminance = Luminance(c);
        return luminance > 0f ? c / luminance : Vector3.One;
    }

    /// <summary>Lens Distortion shader parameters: (center, axis multipliers), (theta, sigma, 1/scale, intensity).</summary>
    public static (Vector4 Center, Vector4 Params) LensDistortion(LensDistortion effect)
    {
        float amount = 1.6f * MathF.Max(MathF.Abs(effect.Intensity * 100f), 1f);
        float theta = MathF.Min(160f, amount) * MathF.PI / 180f;
        float sigma = 2f * MathF.Tan(theta * 0.5f);
        Vector2 center = effect.Center * 2f - Vector2.One;
        return (
            new Vector4(center.X, center.Y, MathF.Max(effect.XMultiplier, 1e-4f), MathF.Max(effect.YMultiplier, 1e-4f)),
            new Vector4(effect.Intensity >= 0f ? theta : 1f / theta, sigma, 1f / MathF.Max(effect.Scale, 0.01f), effect.Intensity * 100f));
    }

    /// <summary>Panini shader parameters: (view extents x, y, distance, crop scale).</summary>
    public static Vector4 Panini(PaniniProjection effect, float fieldOfViewY, float aspect)
    {
        float tanHalf = MathF.Tan(fieldOfViewY * 0.5f);
        var viewExtents = new Vector2(tanHalf * aspect, tanHalf);

        // Size of the projected image edge, to crop it back to the screen.
        float d = effect.Distance;
        float projHyp = MathF.Sqrt(viewExtents.X * viewExtents.X + 1f);
        float cylDistMinusD = 1f / projHyp;
        float cylDist = cylDistMinusD + d;
        Vector2 cropExtents = viewExtents * cylDistMinusD * ((1f + d) / cylDist);

        float scale = MathF.Min(cropExtents.X / viewExtents.X, cropExtents.Y / viewExtents.Y);
        float paniniS = 1f + (Math.Clamp(scale, 0f, 1f) - 1f) * effect.CropToFit;
        return new Vector4(viewExtents.X, viewExtents.Y, d, paniniS);
    }

    /// <summary>
    /// Bokeh sample offsets in the unit disk, shaped like the aperture: rings of points bent into a
    /// polygon of <paramref name="bladeCount"/> sides (Sousa, CryEngine 3 Graphics Gems).
    /// </summary>
    public static Vector2[] BokehKernel(int bladeCount, float bladeCurvature, float bladeRotationDegrees)
    {
        const int rings = 4;
        const int pointsPerRing = 7;
        var kernel = new List<Vector2>();
        float blades = Math.Max(3, bladeCount);
        float curvature = 1f - Math.Clamp(bladeCurvature, 0f, 1f);
        float rotation = bladeRotationDegrees * MathF.PI / 180f;

        for (int ring = 1; ring < rings; ring++)
        {
            float bias = 1f / pointsPerRing;
            float radius = (ring + bias) / (rings - 1f + bias);
            int points = ring * pointsPerRing;
            for (int point = 0; point < points; point++)
            {
                float phi = 2f * MathF.PI * point / points;
                float nt = MathF.Cos(MathF.PI / blades);
                float dt = MathF.Cos(phi - MathF.Tau / blades * MathF.Floor((blades * phi + MathF.PI) / MathF.Tau));
                float r = radius * MathF.Pow(nt / dt, curvature);
                kernel.Add(new Vector2(r * MathF.Cos(phi - rotation), r * MathF.Sin(phi - rotation)));
            }
        }
        return kernel.ToArray();
    }

    /// <summary>
    /// Largest bokeh blur radius, as a fraction of the screen height (14 pixels, at most 5%).
    /// </summary>
    public static float MaxBokehRadius(int screenHeight) => MathF.Min(0.05f, 14f / Math.Max(1, screenHeight));

    /// <summary>
    /// Thin-lens circle of confusion factor: blur radius (fraction of the screen height) =
    /// factor × (depth − focus) / depth. Uses a 24 mm high sensor.
    /// </summary>
    public static float LensCoefficient(float focalLengthMm, float aperture, float focusDistance)
    {
        const float filmHeight = 0.024f;
        float f = focalLengthMm / 1000f;
        float s1 = MathF.Max(focusDistance, f);
        return f * f / (MathF.Max(aperture, 0.1f) * MathF.Max(s1 - f, 1e-4f) * filmHeight * 2f);
    }

    /// <summary>Samples along the motion vector per quality level.</summary>
    public static int MotionBlurSamples(MotionBlurQuality quality) => quality switch
    {
        MotionBlurQuality.Low => 4,
        MotionBlurQuality.Medium => 8,
        _ => 12,
    };

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);
}
