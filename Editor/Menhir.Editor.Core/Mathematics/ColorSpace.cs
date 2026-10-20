using System.Numerics;

namespace Menhir.Editor.Mathematics;

/// <summary>
/// Conversions between linear colors (stored in components, used for lighting) and
/// gamma-encoded colors (what color pickers show). Uses the same 2.2 gamma as the
/// viewport shader so the picker matches what is on screen. Alpha is left as is.
/// </summary>
public static class ColorSpace
{
    private const float Gamma = 2.2f;

    public static Vector4 LinearToGamma(Vector4 color) =>
        new(Encode(color.X), Encode(color.Y), Encode(color.Z), color.W);

    public static Vector4 GammaToLinear(Vector4 color) =>
        new(Decode(color.X), Decode(color.Y), Decode(color.Z), color.W);

    private static float Encode(float value) => MathF.Pow(MathF.Max(value, 0f), 1f / Gamma);

    private static float Decode(float value) => MathF.Pow(MathF.Max(value, 0f), Gamma);
}
