using System.Numerics;
using Menhir.Editor.Inspection;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Serialization;

public enum TestMode
{
    Off,
    FastForward,
}

/// <summary>A component with one property of every supported type.</summary>
public sealed class TestComponent : Component
{
    public bool Flag { get; set; }
    public int Count { get; set; }
    public float Speed { get; set; }
    public string Label { get; set; } = "";
    public Vector2 Size { get; set; }
    public Vector3 Offset { get; set; }
    [Color] public Vector4 Tint { get; set; }
    public TestMode Mode { get; set; }
    [HideInInspector] public long Hidden { get; set; }
    public int ReadOnly => 7;
}
