using System.Numerics;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Scenes;

public enum CameraClearFlags
{
    Skybox,
    SolidColor,
}

/// <summary>
/// Where the game renders the scene from, looking along the entity's forward axis (-Z). The first
/// camera of a scene is used; a scene without one is seen from (0, 1, 10) looking at the origin.
/// The editor's viewport keeps its own camera.
/// </summary>
[ComponentCategory("Rendering")]
public sealed class Camera : Component
{
    [Range(1, 179)]
    [Tooltip("Vertical field of view, in degrees.")]
    public float FieldOfView { get; set; } = 60f;

    [Min(0.01f)]
    public float NearClipPlane { get; set; } = 0.3f;

    [Min(0.02f)]
    public float FarClipPlane { get; set; } = 1000f;

    public CameraClearFlags ClearFlags { get; set; } = CameraClearFlags.Skybox;

    /// <summary>Linear color behind everything when <see cref="ClearFlags"/> is SolidColor; Unity's default blue.</summary>
    [Color]
    public Vector4 Background { get; set; } = ColorSpace.GammaToLinear(new Vector4(0.1921569f, 0.3019608f, 0.4745098f, 1f));
}
