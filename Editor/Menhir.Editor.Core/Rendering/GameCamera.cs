using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Rendering;

/// <summary>What the game's camera sees (the player computes the same in mh_scene.c).</summary>
public readonly record struct GameView(Matrix4x4 View, Matrix4x4 Projection, Vector3 Position, float FieldOfViewY,
    float Near, float Far, CameraClearFlags ClearFlags, Vector3 Background);

public static class GameCamera
{
    /// <summary>The scene's first <see cref="Camera"/>, or one at (0, 1, 10) looking down -Z.</summary>
    public static GameView Compute(Scene scene, float aspect)
    {
        Matrix4x4 world = Matrix4x4.CreateTranslation(0, 1, 10);
        var camera = new Camera();
        Vector3 background = Vector3.Zero;
        if (scene.Entities.FirstOrDefault(e => e.GetComponent<Camera>() != null) is { } entity)
        {
            world = scene.GetWorldMatrix(entity);
            camera = entity.GetComponent<Camera>()!;
            background = new Vector3(camera.Background.X, camera.Background.Y, camera.Background.Z);
        }
        else
            camera.ClearFlags = CameraClearFlags.Skybox;

        float fov = Math.Clamp(camera.FieldOfView, 1f, 179f) * MathF.PI / 180f;
        float near = MathF.Max(camera.NearClipPlane, 0.01f);
        float far = MathF.Max(camera.FarClipPlane, near + 0.01f);
        Vector3 position = world.Translation;
        Vector3 forward = SceneEnvironment.Forward(world);
        Vector3 up = Vector3.TransformNormal(Vector3.UnitY, world);
        up = up.LengthSquared() > 1e-12f ? Vector3.Normalize(up) : Vector3.UnitY;

        return new GameView(Matrix4x4.CreateLookAt(position, position + forward, up),
            Projections.Perspective(fov, aspect, near, far), position, fov, near, far, camera.ClearFlags, background);
    }
}
