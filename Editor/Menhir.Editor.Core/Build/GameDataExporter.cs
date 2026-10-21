using System.Numerics;
using Menhir.Editor.Assets;
using Menhir.Editor.Input;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Build;

/// <summary>A scene to put in the build, under the name the game knows it by.</summary>
public sealed record BuildScene(string Name, Scene Scene);

/// <summary>
/// Writes GameData.binchunk: the game settings, every scene of the build and the meshes and
/// textures they use. Chunk layouts are in Docs/BinChunk.md. Values that only depend on the
/// components' settings (ambient light, post-processing parameters) are computed here, with the
/// editor's code; what depends on the camera or the window (shadow cascades, aspect ratio) is
/// computed by the game each frame.
/// </summary>
public static class GameDataExporter
{
    public const int BokehKernelSize = 42;

    /// <summary>First chunk id of imported models; built-in meshes use 1 + <see cref="PrimitiveMesh"/>.</summary>
    public const uint FirstModelId = 1000;

    /// <param name="loadTexture">A texture asset's image, or null when it cannot be loaded.</param>
    /// <param name="loadModel">A model asset's mesh; throws (InvalidData, NotSupported, IO) when it cannot be imported.</param>
    public static byte[] Export(string title, IReadOnlyList<BuildScene> scenes, RenderingSettings rendering,
        BuildSettings build, Func<Guid, ImageData?> loadTexture, Func<Guid, MeshData> loadModel, List<string> warnings,
        InputSettings? input = null)
    {
        if (scenes.Count == 0)
            throw new ArgumentException("A build needs at least one scene.", nameof(scenes));

        var file = new BinChunkWriter();
        file.Add(BinChunk.Game, 0, GameChunk(title, scenes.Count, rendering, build, input ?? new InputSettings(), warnings));

        var meshes = new Meshes(file, loadModel);
        var textures = new Dictionary<object, uint>(); // FilmGrainType or texture Guid -> chunk id
        for (int i = 0; i < scenes.Count; i++)
            file.Add(BinChunk.Scene, (uint)i, SceneChunk(scenes[i], scenes, meshes, textures, file, loadTexture, warnings));
        return file.ToArray();
    }

    public static uint MeshId(PrimitiveMesh mesh) => (uint)mesh + 1;

    private static byte[] GameChunk(string title, int sceneCount, RenderingSettings rendering, BuildSettings build,
        InputSettings input, List<string> warnings)
    {
        var w = new ChunkWriter();
        w.U32((uint)Math.Clamp(build.WindowWidth, 64, 7680));
        w.U32((uint)Math.Clamp(build.WindowHeight, 64, 4320));
        w.String(title);
        w.U32((uint)sceneCount);

        w.Bool(rendering.MainLightShadows);
        w.U32((uint)rendering.MainLightShadowResolution);
        w.Bool(rendering.AdditionalLightShadows);
        w.U32((uint)rendering.AdditionalLightShadowResolution);
        w.F32(rendering.MaxDistance);
        float[] ends = rendering.CascadeEnds();
        w.U32((uint)ends.Length);
        for (int i = 0; i < 4; i++)
            w.F32(i < ends.Length ? ends[i] : 1f);
        w.F32(rendering.LastBorder);
        w.F32(rendering.DepthBias);
        w.F32(rendering.NormalBias);
        w.U32((uint)ShadowSetup.FilterTaps(LightShadows.Soft, rendering));

        w.U32((uint)input.Axes.Count);
        foreach (InputAxis axis in input.Axes)
        {
            int Code(string name)
            {
                if (InputCodes.TryParse(name, out int code))
                    return code;
                warnings.Add($"Input axis {axis.Name}: '{name}' is not a key or button name; it is left out.");
                return InputCodes.None;
            }

            w.String(axis.Name);
            w.U32((uint)axis.Type);
            w.I32(Code(axis.NegativeButton));
            w.I32(Code(axis.PositiveButton));
            w.I32(Code(axis.AltNegativeButton));
            w.I32(Code(axis.AltPositiveButton));
            w.F32(MathF.Max(axis.Gravity, 0f));
            w.F32(MathF.Max(axis.Dead, 0f));
            w.F32(MathF.Max(axis.Sensitivity, 0f));
            w.Bool(axis.Snap);
            w.Bool(axis.Invert);
            w.U32((uint)Math.Clamp(axis.Axis, 0, 5));
        }
        return w.ToArray();
    }

    /// <summary>The meshes written so far (each once), by built-in mesh or model GUID.</summary>
    private sealed class Meshes(BinChunkWriter file, Func<Guid, MeshData> loadModel)
    {
        private readonly Dictionary<object, (uint Id, Bounds Bounds)?> _written = new();

        /// <summary>The chunk id and bounds of the renderer's mesh, or null (with the reason) when it has none.</summary>
        public (uint Id, Bounds Bounds)? Get(MeshRenderer renderer, out string? error)
        {
            error = null;
            object key = renderer.Model is { } guid ? guid : renderer.Mesh;
            if (_written.TryGetValue(key, out var known))
            {
                if (known == null)
                    error = "its model cannot be imported";
                return known;
            }

            MeshData data;
            uint id;
            try
            {
                (data, id) = renderer.Model is { } model
                    ? (loadModel(model), FirstModelId + (uint)_written.Keys.Count(k => k is Guid))
                    : (Primitive(renderer.Mesh), MeshId(renderer.Mesh));
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException
                                          or UnauthorizedAccessException)
            {
                error = $"its model cannot be imported ({e.Message})";
                _written[key] = null;
                return null;
            }
            file.Add(BinChunk.Mesh, id, MeshChunk(data));
            _written[key] = (id, data.Bounds);
            return (id, data.Bounds);
        }
    }

    private static byte[] SceneChunk(BuildScene buildScene, IReadOnlyList<BuildScene> allScenes, Meshes meshes, Dictionary<object, uint> textures,
        BinChunkWriter file, Func<Guid, ImageData?> loadTexture, List<string> warnings)
    {
        Scene scene = buildScene.Scene;
        SceneEnvironment environment = SceneEnvironment.Gather(scene);
        var w = new ChunkWriter();
        w.String(buildScene.Name);

        ProceduralSkybox sky = environment.Sky;
        w.U32((uint)sky.SunDisk);
        w.F32(sky.SunSize);
        w.F32(sky.SunSizeConvergence);
        w.F32(sky.AtmosphereThickness);
        w.Vec3(Xyz(ColorSpace.LinearToGamma(sky.SkyTint)));
        w.Vec3(Xyz(sky.GroundColor));
        w.F32(sky.Exposure);
        w.Vec3(environment.AmbientTop);
        w.Vec3(environment.AmbientHorizon);
        w.Vec3(environment.AmbientBottom);

        IReadOnlyList<Entity> entities = scene.Entities;
        var index = new Dictionary<long, int>();
        for (int i = 0; i < entities.Count; i++)
            index[entities[i].LocalId] = i;
        w.U32((uint)entities.Count);
        foreach (Entity entity in entities)
        {
            w.I32(index.TryGetValue(entity.ParentId, out int parent) ? parent : -1);
            w.Vec3(entity.Transform.Position);
            w.Quat(entity.Transform.Rotation);
            w.Vec3(entity.Transform.Scale);
        }

        var renderers = new List<(int Entity, MeshRenderer Renderer, uint Mesh, Bounds Bounds)>();
        foreach ((int entity, MeshRenderer renderer) in Components<MeshRenderer>(entities))
        {
            if (meshes.Get(renderer, out string? error) is var (mesh, bounds))
                renderers.Add((entity, renderer, mesh, bounds));
            else
                warnings.Add($"{buildScene.Name}: the Mesh Renderer of {entities[entity].Name} is left out: {error}.");
        }
        w.U32((uint)renderers.Count);
        foreach ((int entity, MeshRenderer renderer, uint mesh, Bounds bounds) in renderers)
        {
            w.U32((uint)entity);
            w.U32(mesh);
            w.Vec3(bounds.Min);
            w.Vec3(bounds.Max);
            w.Vec4(renderer.Color);
            w.F32(renderer.Metallic);
            w.F32(renderer.Smoothness);
            w.Bool(renderer.CastShadows);
            w.Bool(renderer.ReceiveShadows);
        }

        var lights = Components<Light>(entities);
        w.U32((uint)lights.Count);
        foreach ((int entity, Light light) in lights)
        {
            w.U32((uint)entity);
            w.U32((uint)light.LightType);
            w.Vec3(Xyz(light.Color) * light.Intensity);
            w.F32(light.Range);
            w.F32(light.SpotAngle);
            w.U32((uint)light.Shadows);
            w.F32(light.ShadowStrength);
        }

        var cameras = Components<Camera>(entities);
        w.U32((uint)cameras.Count);
        foreach ((int entity, Camera camera) in cameras)
        {
            w.U32((uint)entity);
            w.F32(camera.FieldOfView);
            w.F32(camera.NearClipPlane);
            w.F32(camera.FarClipPlane);
            w.U32((uint)camera.ClearFlags);
            w.Vec3(Xyz(camera.Background));
        }

        uint TextureId(object key, Func<byte[]> chunk)
        {
            if (!textures.TryGetValue(key, out uint id))
            {
                id = (uint)textures.Count + 1;
                file.Add(BinChunk.Texture, id, chunk());
                textures[key] = id;
            }
            return id;
        }

        uint lut = 0;
        float lutContribution = 0f;
        if (environment.PostProcessing.ColorLookup is { Contribution: > 0f, Texture: { } guid } lookup)
        {
            ImageData? image = loadTexture(guid);
            if (image == null)
                warnings.Add($"{buildScene.Name}: the Color Lookup texture could not be loaded; the effect is left out.");
            else if (image.Width != image.Height * image.Height)
                warnings.Add($"{buildScene.Name}: the Color Lookup texture is {image.Width}×{image.Height}, not a strip of " +
                             "N tiles of N×N; the effect is left out.");
            else
            {
                lut = TextureId(guid, () => TextureChunk(image.Width, image.Height, 4, repeat: false, image.Rgba));
                lutContribution = lookup.Contribution;
            }
        }

        WritePostProcessing(w, environment.PostProcessing, lut, lutContribution,
            type => TextureId(type, () => TextureChunk(FilmGrainNoise.Size, FilmGrainNoise.Size, 1, repeat: true,
                FilmGrainNoise.Generate(type))));

        var flyCameras = Components<FlyCamera>(entities);
        w.U32((uint)flyCameras.Count);
        foreach ((int entity, FlyCamera fly) in flyCameras)
        {
            Vector3 euler = EulerAngles.FromQuaternion(entities[entity].Transform.Rotation);
            w.U32((uint)entity);
            w.F32(fly.MoveSpeed);
            w.F32(fly.LookSpeed);
            w.F32(MathF.Max(fly.FastMultiplier, 1f));
            w.F32(euler.Y);
            w.F32(euler.X);
            w.String(fly.HorizontalAxis);
            w.String(fly.VerticalAxis);
            w.String(fly.LookXAxis);
            w.String(fly.LookYAxis);
            w.String(fly.FastButton);
            w.Bool(fly.RequireRightMouseButton);
        }

        var switchers = new List<(int Entity, SceneSwitcher Switcher, int Scene)>();
        foreach ((int entity, SceneSwitcher switcher) in Components<SceneSwitcher>(entities))
        {
            int target = allScenes.ToList().FindIndex(s => s.Name == switcher.Scene.Trim());
            if (target < 0)
                warnings.Add($"{buildScene.Name}: the Scene Switcher of {entities[entity].Name} loads '{switcher.Scene}', " +
                             "which is not in the build; it is left out.");
            else
                switchers.Add((entity, switcher, target));
        }
        w.U32((uint)switchers.Count);
        foreach ((int entity, SceneSwitcher switcher, int target) in switchers)
        {
            w.U32((uint)entity);
            w.String(switcher.Button);
            w.U32((uint)target);
            w.U32((uint)switcher.Mode);
        }
        return w.ToArray();
    }

    /// <summary>The settings of each pass of GLPostProcessor, in the order it runs them.</summary>
    private static void WritePostProcessing(ChunkWriter w, PostProcessStack stack, uint lut, float lutContribution,
        Func<FilmGrainType, uint> grainTexture)
    {
        DepthOfField? dof = stack.DepthOfField is { Mode: not DepthOfFieldMode.Off } d ? d : null;
        w.U32((uint)(dof?.Mode ?? DepthOfFieldMode.Off));
        w.F32(dof?.GaussianStart ?? 0f);
        w.F32(dof != null ? MathF.Max(dof.GaussianEnd, dof.GaussianStart + 0.01f) : 0f);
        w.F32(dof?.GaussianMaxRadius ?? 0f);
        w.Bool(dof?.HighQualitySampling ?? false);
        w.F32(dof?.FocusDistance ?? 0f);
        w.F32(dof != null ? PostProcessMath.LensCoefficient(dof.FocalLength, dof.Aperture, dof.FocusDistance) : 0f);
        Vector2[] kernel = dof != null
            ? PostProcessMath.BokehKernel(dof.BladeCount, dof.BladeCurvature, dof.BladeRotation)
            : new Vector2[BokehKernelSize];
        if (kernel.Length != BokehKernelSize)
            throw new InvalidOperationException($"The bokeh kernel has {kernel.Length} samples, not {BokehKernelSize}.");
        foreach (Vector2 sample in kernel)
            w.Vec2(sample);

        MotionBlur? motion = stack.MotionBlur is { Intensity: > 0f } m ? m : null;
        w.Bool(motion != null);
        w.F32(motion?.Intensity ?? 0f);
        w.F32(motion?.Clamp ?? 0f);
        w.U32((uint)(motion != null ? PostProcessMath.MotionBlurSamples(motion.Quality) : 0));

        PaniniProjection? panini = stack.PaniniProjection is { Distance: > 0f } p ? p : null;
        w.Bool(panini != null);
        w.F32(panini?.Distance ?? 0f);
        w.F32(panini?.CropToFit ?? 0f);

        Bloom? bloom = stack.Bloom is { Intensity: > 0f } b ? b : null;
        float threshold = MathF.Pow(MathF.Max(bloom?.Threshold ?? 0f, 0f), 2.2f);
        w.Bool(bloom != null);
        w.U32(bloom?.Downscale == BloomDownscale.Quarter ? 4u : 2u);
        w.U32((uint)Math.Clamp(bloom?.MaxIterations ?? 1, 1, 16));
        w.F32(threshold);
        w.F32(threshold * 0.5f);
        w.F32(bloom?.Clamp ?? 0f);
        w.F32(0.05f + (0.95f - 0.05f) * Math.Clamp(bloom?.Scatter ?? 0f, 0f, 1f));
        w.Bool(bloom?.HighQualityFiltering ?? false);
        w.F32(bloom?.Intensity ?? 0f);
        w.Vec3(bloom != null ? PostProcessMath.BloomTint(bloom.Tint) : Vector3.One);

        Vignette? vignette = stack.Vignette is { Intensity: > 0f } v ? v : null;
        w.Bool(vignette != null);
        w.Vec3(vignette != null ? Xyz(vignette.Color) : Vector3.Zero);
        w.Vec2(vignette?.Center ?? Vector2.Zero);
        w.F32((vignette?.Intensity ?? 0f) * 3f);
        w.F32((vignette?.Smoothness ?? 0f) * 5f);
        w.Bool(vignette?.Rounded ?? false);

        w.U32(lut);
        w.F32(lutContribution);

        FilmGrain? grain = stack.FilmGrain is { Intensity: > 0f } g ? g : null;
        w.U32(grain != null ? grainTexture(grain.GrainType) : 0u);
        w.F32((grain?.Intensity ?? 0f) * 4f);
        w.F32(grain?.Response ?? 0f);

        List<ShaderUniform> uniforms = PostProcessUniforms.Uber(stack);
        w.U32((uint)uniforms.Count);
        foreach (ShaderUniform uniform in uniforms)
        {
            w.String(uniform.Name);
            w.U32((uint)uniform.Kind);
            w.Vec4(uniform.Value);
        }

        w.Bool(stack.ColorCurves != null);
        if (stack.ColorCurves != null)
            foreach (float value in PostProcessUniforms.BakeCurves(stack.ColorCurves))
                w.F32(value);
    }

    private static MeshData Primitive(PrimitiveMesh mesh) => mesh switch
    {
        PrimitiveMesh.Cube => Primitives.CreateCube(),
        _ => throw new NotSupportedException($"No mesh data for {mesh}."),
    };

    private static byte[] MeshChunk(MeshData data)
    {
        var w = new ChunkWriter();
        w.U32((uint)data.Positions.Length);
        w.U32((uint)data.Indices.Length);
        for (int i = 0; i < data.Positions.Length; i++)
        {
            w.Vec3(data.Positions[i]);
            w.Vec3(data.Normals[i]);
        }
        foreach (uint i in data.Indices)
            w.U32(i);
        return w.ToArray();
    }

    /// <param name="channels">1 (R8) or 4 (RGBA8).</param>
    private static byte[] TextureChunk(int width, int height, int channels, bool repeat, byte[] pixels)
    {
        var w = new ChunkWriter();
        w.U32((uint)width);
        w.U32((uint)height);
        w.U32((uint)channels);
        w.Bool(repeat);
        w.Bytes(pixels);
        return w.ToArray();
    }

    /// <summary>The first component of a kind on each entity, like the editor's renderer uses them.</summary>
    private static List<(int Entity, T Component)> Components<T>(IReadOnlyList<Entity> entities) where T : Component
    {
        var result = new List<(int, T)>();
        for (int i = 0; i < entities.Count; i++)
            if (entities[i].GetComponent<T>() is { } component)
                result.Add((i, component));
        return result;
    }

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);
}
