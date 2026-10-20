using System.Numerics;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Lighting;

/// <summary>Where the viewport camera is, for fitting shadow cascades around what it sees.</summary>
/// <param name="CameraToWorld">Inverse of the view matrix.</param>
public readonly record struct CameraInfo(Matrix4x4 CameraToWorld, float FieldOfViewY, float Aspect, float Near);

/// <summary>One shadow map render: a cascade of the sun, a spot light, or one face of a point light.</summary>
/// <param name="ViewProjection">World to the light's clip space, to render the casters.</param>
/// <param name="WorldToShadow">World to shadow map coordinates (atlas uv in xy, depth in z, both 0..1; divide by w).</param>
/// <param name="AtlasRect">The slice's tile in the atlas: min uv (xy), max uv (zw).</param>
/// <param name="AtlasPixels">The tile in pixels: x, y, size, size.</param>
/// <param name="DepthBias">World distance casters move away from the light (negative, URP style).</param>
/// <param name="NormalBias">World distance casters shrink along their normals (negative).</param>
public readonly record struct ShadowSlice(Matrix4x4 ViewProjection, Matrix4x4 WorldToShadow, Vector4 AtlasRect,
    (int X, int Y, int Size) AtlasPixels, float DepthBias, float NormalBias);

/// <summary>The sun's shadow cascades.</summary>
/// <param name="CascadeSpheres">Per cascade: center (xyz) and squared radius (w); a point uses the first sphere it is in.</param>
/// <param name="Filter">0 for hard shadows, else the soft filter's taps per side (2, 3 or 4).</param>
public sealed record MainLightShadows(int AtlasSize, IReadOnlyList<ShadowSlice> Cascades, IReadOnlyList<Vector4> CascadeSpheres,
    float Strength, int Filter, float MaxDistance, float FadeBorder);

/// <summary>Shadows of the point and spot lights, in one atlas.</summary>
/// <param name="FirstSlice">Per additional light (same order as the environment's): first slice, or -1 without shadows.</param>
public sealed record AdditionalLightShadows(int AtlasSize, IReadOnlyList<ShadowSlice> Slices, int[] FirstSlice,
    float[] Strength, int[] Filter);

/// <summary>
/// Places the shadow maps, the way URP does: the sun's view is split into cascades, each fitted
/// with a bounding sphere (stable as the camera turns) and snapped to shadow map texels (stable
/// as it moves). Spot lights get one perspective slice and point lights six (cube faces).
/// </summary>
public static class ShadowSetup
{
    /// <summary>Most slices in the additional lights' atlas (a point light takes 6).</summary>
    public const int MaxAdditionalSlices = 16;

    // The soft filter reaches about 2.5 texels, so biases grow with it (URP's kernelRadius).
    private const float SoftKernelRadius = 2.5f;

    /// <summary>Cube face directions and up vectors, in the order the shader picks them (+X, -X, +Y, -Y, +Z, -Z).</summary>
    public static readonly (Vector3 Forward, Vector3 Up)[] CubeFaces =
    [
        (Vector3.UnitX, -Vector3.UnitY), (-Vector3.UnitX, -Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitY, -Vector3.UnitZ),
        (Vector3.UnitZ, -Vector3.UnitY), (-Vector3.UnitZ, -Vector3.UnitY),
    ];

    public static int FilterTaps(LightShadows shadows, RenderingSettings settings) =>
        shadows != LightShadows.Soft || !settings.SoftShadows ? 0 : settings.SoftShadowQuality switch
        {
            SoftShadowQuality.Low => 2,
            SoftShadowQuality.Medium => 3,
            _ => 4,
        };

    /// <summary>The sun's cascades, or null when it casts no shadows (or nothing casts any).</summary>
    public static MainLightShadows? MainLight(SceneEnvironment environment, CameraInfo camera, RenderingSettings settings,
        Bounds? casters)
    {
        if (!settings.MainLightShadows || environment.SunShadows == LightShadows.None ||
            environment.SunColor == Vector3.Zero || casters is not { } casterBounds)
            return null;

        int atlasSize = (int)settings.MainLightShadowResolution;
        float[] ends = settings.CascadeEnds();
        int count = ends.Length;
        int tileSize = count == 1 ? atlasSize : atlasSize / 2;
        int filter = FilterTaps(environment.SunShadows, settings);
        float maxDistance = MathF.Max(settings.MaxDistance, camera.Near + 0.1f);
        Vector3 toSun = Vector3.Normalize(environment.ToSun);

        var cascades = new List<ShadowSlice>(count);
        var spheres = new List<Vector4>(count);
        float start = camera.Near;
        for (int i = 0; i < count; i++)
        {
            float end = MathF.Max(ends[i] * maxDistance, start + 0.01f);
            (Vector3 center, float radius) = SliceSphere(camera, start, end);
            // Quantized so it does not change with the camera's rotation (stable shadow edges).
            radius = MathF.Ceiling(radius * 16f) / 16f;

            // Snap the center to whole texels in the light's view.
            Matrix4x4 lightRotation = Matrix4x4.CreateLookAt(Vector3.Zero, -toSun, UpFor(-toSun));
            float texel = 2f * radius / tileSize;
            Vector3 lightSpace = Vector3.Transform(center, lightRotation);
            lightSpace.X = MathF.Floor(lightSpace.X / texel) * texel;
            lightSpace.Y = MathF.Floor(lightSpace.Y / texel) * texel;
            Matrix4x4.Invert(lightRotation, out Matrix4x4 lightToWorld);
            center = Vector3.Transform(lightSpace, lightToWorld);

            // Back far enough towards the sun to catch every caster, even outside the camera's view.
            float back = radius;
            foreach (Vector3 corner in casterBounds.Corners())
                back = MathF.Max(back, Vector3.Dot(corner - center, toSun));
            back += 1f;

            Vector3 eye = center + toSun * back;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, center, UpFor(-toSun));
            Matrix4x4 projection = Projections.Orthographic(radius, radius, 0f, back + radius);
            int column = i % 2, row = i / 2;
            var pixels = count == 1 ? (0, 0, atlasSize) : (column * tileSize, row * tileSize, tileSize);

            float bias = filter > 0 ? SoftKernelRadius : 1f;
            cascades.Add(Slice(view * projection, pixels, atlasSize,
                -settings.DepthBias * texel * bias, -settings.NormalBias * texel * bias));
            spheres.Add(new Vector4(center, radius * radius));
            start = end;
        }

        return new MainLightShadows(atlasSize, cascades, spheres, environment.SunShadowStrength, filter,
            maxDistance, Math.Clamp(settings.LastBorder, 0.001f, 1f));
    }

    /// <summary>Shadows of the point and spot lights that have them, until the atlas is full.</summary>
    public static AdditionalLightShadows? AdditionalLights(SceneEnvironment environment, RenderingSettings settings, Bounds? casters)
    {
        IReadOnlyList<PunctualLight> lights = environment.AdditionalLights;
        if (!settings.AdditionalLightShadows || casters == null || !lights.Any(l => l.Shadows != LightShadows.None))
            return null;

        // Decide which lights fit, then size the tiles for that many slices.
        var firstSlice = new int[lights.Count];
        int sliceCount = 0;
        for (int i = 0; i < lights.Count; i++)
        {
            int needed = lights[i].IsSpot ? 1 : 6;
            if (lights[i].Shadows == LightShadows.None || sliceCount + needed > MaxAdditionalSlices)
            {
                firstSlice[i] = -1;
                continue;
            }
            firstSlice[i] = sliceCount;
            sliceCount += needed;
        }
        if (sliceCount == 0)
            return null;

        int atlasSize = (int)settings.AdditionalLightShadowResolution;
        int grid = (int)MathF.Ceiling(MathF.Sqrt(sliceCount));
        int tileSize = atlasSize / grid;
        var slices = new List<ShadowSlice>(sliceCount);
        var strength = new float[lights.Count];
        var filter = new int[lights.Count];

        for (int i = 0; i < lights.Count; i++)
        {
            if (firstSlice[i] < 0)
                continue;
            PunctualLight light = lights[i];
            strength[i] = light.ShadowStrength;
            filter[i] = FilterTaps(light.Shadows, settings);
            float near = MathF.Max(0.05f, light.Range * 0.005f);
            float kernel = filter[i] > 0 ? SoftKernelRadius : 1f;

            void Add(Vector3 forward, Vector3 up, float fieldOfView)
            {
                int index = slices.Count;
                var pixels = (index % grid * tileSize, index / grid * tileSize, tileSize);
                Matrix4x4 view = Matrix4x4.CreateLookAt(light.Position, light.Position + forward, up);
                Matrix4x4 projection = Projections.Perspective(fieldOfView, 1f, near, light.Range);
                // Texel size one unit away from the light, like URP for punctual lights.
                float texel = 2f * MathF.Tan(fieldOfView / 2f) / tileSize;
                slices.Add(Slice(view * projection, pixels, atlasSize,
                    -settings.DepthBias * texel * kernel, -settings.NormalBias * texel * kernel));
            }

            if (light.IsSpot)
            {
                float coneAngle = 2f * MathF.Acos(Math.Clamp(light.SpotCosOuter, -1f, 1f));
                Add(light.Direction, UpFor(light.Direction), MathF.Min(coneAngle + 0.05f, MathF.PI * 0.95f));
            }
            else
            {
                // A little wider than 90°, so filtering near a face's edge stays inside the face.
                float fieldOfView = 2f * MathF.Atan(1f + 6f / tileSize);
                foreach ((Vector3 forward, Vector3 up) in CubeFaces)
                    Add(forward, up, fieldOfView);
            }
        }

        return new AdditionalLightShadows(atlasSize, slices, firstSlice, strength, filter);
    }

    /// <summary>Sphere around the part of the camera's view between two distances.</summary>
    public static (Vector3 Center, float Radius) SliceSphere(CameraInfo camera, float start, float end)
    {
        float tanY = MathF.Tan(camera.FieldOfViewY / 2f);
        float tanX = tanY * camera.Aspect;
        Span<Vector3> corners = stackalloc Vector3[8];
        int n = 0;
        foreach (float d in new[] { start, end })
        {
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sy in new[] { -1f, 1f })
                corners[n++] = Vector3.Transform(new Vector3(sx * tanX * d, sy * tanY * d, -d), camera.CameraToWorld);
        }

        Vector3 center = Vector3.Zero;
        foreach (Vector3 c in corners)
            center += c;
        center /= 8f;
        float radius = 0f;
        foreach (Vector3 c in corners)
            radius = MathF.Max(radius, Vector3.Distance(center, c));
        return (center, radius);
    }

    private static ShadowSlice Slice(Matrix4x4 viewProjection, (int X, int Y, int Size) pixels, int atlasSize,
        float depthBias, float normalBias)
    {
        float scale = pixels.Size / (float)atlasSize;
        float u = pixels.X / (float)atlasSize, v = pixels.Y / (float)atlasSize;

        // Clip space (-1..1) to 0..1, then into the slice's tile (depth stays 0..1).
        Matrix4x4 toTexture = Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);
        Matrix4x4 toTile = Matrix4x4.CreateScale(scale, scale, 1f) * Matrix4x4.CreateTranslation(u, v, 0f);
        return new ShadowSlice(viewProjection, viewProjection * toTexture * toTile,
            new Vector4(u, v, u + scale, v + scale), pixels, depthBias, normalBias);
    }

    /// <summary>An up vector that is not parallel to <paramref name="forward"/>.</summary>
    private static Vector3 UpFor(Vector3 forward) =>
        MathF.Abs(Vector3.Dot(Vector3.Normalize(forward), Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
}
