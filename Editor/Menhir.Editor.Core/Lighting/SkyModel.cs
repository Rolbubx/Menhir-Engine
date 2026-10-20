using System.Numerics;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Lighting;

/// <summary>
/// The procedural sky: single scattering through a thin atmosphere (Sean O'Neil's model,
/// GPU Gems 2 ch. 16), with the constants and look of Unity's Skybox/Procedural.
/// The skybox shader runs the same computation per pixel; this C# copy gives the
/// ambient light of the scene and is what the unit tests check.
/// </summary>
public static class SkyModel
{
    private static readonly Vector3 DefaultWavelength = new(0.65f, 0.57f, 0.475f);
    private static readonly Vector3 WavelengthRange = new(0.15f, 0.15f, 0.15f);

    private const float OuterRadius = 1.025f;
    private const float InnerRadius = 1f;
    private const float CameraHeight = 0.0001f;
    private const float Mie = 0.0010f;
    private const float SunBrightness = 20f;
    private const float MaxScatter = 50f;
    private const float Scale = 1f / (OuterRadius - 1f);
    private const float ScaleDepth = 0.25f;
    private const float ScaleOverScaleDepth = Scale / ScaleDepth;
    private const int Samples = 2;
    private const float SkyGroundThreshold = 0.02f;

    /// <summary>
    /// Sky color seen along <paramref name="direction"/> (from the camera, normalized), without the sun disk.
    /// <paramref name="toSun"/> points towards the sun.
    /// </summary>
    public static Vector3 Evaluate(ProceduralSkybox sky, Vector3 direction, Vector3 toSun)
    {
        Scatter(sky, direction, toSun, out Vector3 skyColor, out Vector3 groundColor, out _);
        // Blend sky and ground over a thin band at the horizon.
        float t = Math.Clamp(-direction.Y / SkyGroundThreshold, 0f, 1f);
        return Vector3.Lerp(skyColor, groundColor, t);
    }

    /// <summary>The scattering integrals for one view direction, as in the skybox shader.</summary>
    /// <param name="sunColor">Color of the sun disk (before its shape is applied).</param>
    public static void Scatter(ProceduralSkybox sky, Vector3 eyeRay, Vector3 toSun,
        out Vector3 skyColor, out Vector3 groundColor, out Vector3 sunColor)
    {
        Vector3 tintGamma = Xyz(ColorSpace.LinearToGamma(sky.SkyTint));
        Vector3 wavelength = Vector3.Lerp(DefaultWavelength - WavelengthRange, DefaultWavelength + WavelengthRange,
            Vector3.One - tintGamma);
        Vector3 invWavelength = Vector3.One / (wavelength * wavelength * wavelength * wavelength);

        float rayleigh = 0.0025f * MathF.Pow(sky.AtmosphereThickness, 2.5f);
        float krESun = rayleigh * SunBrightness;
        float kr4Pi = rayleigh * 4f * MathF.PI;
        float kmESun = Mie * SunBrightness;
        float km4Pi = Mie * 4f * MathF.PI;
        Vector3 extinction = invWavelength * kr4Pi + new Vector3(km4Pi);

        Vector3 cameraPos = new(0, InnerRadius + CameraHeight, 0);
        Vector3 cIn, cOut;

        if (eyeRay.Y >= 0f)
        {
            // Looking at the sky: integrate along the ray up to the top of the atmosphere.
            float far = MathF.Sqrt(OuterRadius * OuterRadius + eyeRay.Y * eyeRay.Y - 1f) - eyeRay.Y;
            float height = InnerRadius + CameraHeight;
            float depth = MathF.Exp(ScaleOverScaleDepth * -CameraHeight);
            float startAngle = Vector3.Dot(eyeRay, cameraPos) / height;
            float startOffset = depth * ScaleFunction(startAngle);

            float sampleLength = far / Samples;
            float scaledLength = sampleLength * Scale;
            Vector3 sampleRay = eyeRay * sampleLength;
            Vector3 samplePoint = cameraPos + sampleRay * 0.5f;

            Vector3 frontColor = Vector3.Zero;
            for (int i = 0; i < Samples; i++)
            {
                float sampleHeight = samplePoint.Length();
                float sampleDepth = MathF.Exp(ScaleOverScaleDepth * (InnerRadius - sampleHeight));
                float lightAngle = Vector3.Dot(toSun, samplePoint) / sampleHeight;
                float cameraAngle = Vector3.Dot(eyeRay, samplePoint) / sampleHeight;
                float scatter = startOffset + sampleDepth * (ScaleFunction(lightAngle) - ScaleFunction(cameraAngle));
                Vector3 attenuate = Exp(-Math.Clamp(scatter, 0f, MaxScatter) * extinction);
                frontColor += attenuate * (sampleDepth * scaledLength);
                samplePoint += sampleRay;
            }
            cIn = frontColor * (invWavelength * krESun);
            cOut = frontColor * kmESun;
        }
        else
        {
            // Looking down: the ground, seen through a sliver of atmosphere.
            float far = -CameraHeight / MathF.Min(-0.001f, eyeRay.Y);
            Vector3 pos = cameraPos + far * eyeRay;
            float depth = MathF.Exp(-CameraHeight / ScaleDepth);
            float cameraScale = ScaleFunction(Vector3.Dot(-eyeRay, pos));
            float lightScale = ScaleFunction(Vector3.Dot(toSun, pos));
            float cameraOffset = depth * cameraScale;
            float temp = lightScale + cameraScale;

            float scaledLength = far * Scale;
            Vector3 samplePoint = cameraPos + eyeRay * far * 0.5f;
            float sampleDepth = MathF.Exp(ScaleOverScaleDepth * (InnerRadius - samplePoint.Length()));
            float scatter = sampleDepth * temp - cameraOffset;
            Vector3 attenuate = Exp(-Math.Clamp(scatter, 0f, MaxScatter) * extinction);
            Vector3 frontColor = attenuate * (sampleDepth * scaledLength);
            cIn = frontColor * (invWavelength * krESun + new Vector3(kmESun));
            cOut = Vector3.Clamp(attenuate, Vector3.Zero, Vector3.One);
        }

        float eyeCos = Vector3.Dot(toSun, eyeRay);
        groundColor = sky.Exposure * (cIn + Xyz(sky.GroundColor) * cOut);
        skyColor = sky.Exposure * (cIn * RayleighPhase(eyeCos * eyeCos));
        float sundiskFactor = sky.SunDisk == SunDisk.Simple ? 27f : 15f;
        sunColor = sundiskFactor * Vector3.Clamp(cOut, Vector3.Zero, Vector3.One);
    }

    /// <summary>
    /// Ambient light as three colors (top, horizon, bottom), averaged over each part of the sky,
    /// like Unity's gradient ambient. Shaders blend them by the surface normal's height.
    /// </summary>
    public static (Vector3 Top, Vector3 Horizon, Vector3 Bottom) Ambient(ProceduralSkybox sky, Vector3 toSun)
    {
        const int azimuths = 8;
        Vector3 top = Vector3.Zero, horizon = Vector3.Zero, bottom = Vector3.Zero;
        for (int i = 0; i < azimuths; i++)
        {
            float angle = i * MathF.Tau / azimuths;
            Vector3 Dir(float y)
            {
                float r = MathF.Sqrt(1 - y * y);
                return new Vector3(MathF.Cos(angle) * r, y, MathF.Sin(angle) * r);
            }
            top += Evaluate(sky, Dir(0.85f), toSun) * 0.6f + Evaluate(sky, Dir(0.4f), toSun) * 0.4f;
            horizon += Evaluate(sky, Dir(0.05f), toSun);
            bottom += Evaluate(sky, Dir(-0.5f), toSun);
        }
        float scale = sky.AmbientIntensity / azimuths;
        return (top * scale, horizon * scale, bottom * scale);
    }

    /// <summary>The sun direction used when a scene has no directional light (about 50° up).</summary>
    public static Vector3 DefaultToSun { get; } = Vector3.Normalize(new Vector3(-0.32f, 0.77f, 0.55f));

    private static float ScaleFunction(float cos)
    {
        float x = 1f - cos;
        return 0.25f * MathF.Exp(-0.00287f + x * (0.459f + x * (3.83f + x * (-6.80f + x * 5.25f))));
    }

    private static float RayleighPhase(float eyeCos2) => 0.75f + 0.75f * eyeCos2;

    private static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);
}
