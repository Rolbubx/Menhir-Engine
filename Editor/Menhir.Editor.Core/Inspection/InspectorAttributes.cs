namespace Menhir.Editor.Inspection;

/// <summary>Keeps a public property out of the inspector.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute;

/// <summary>Shows a Vector4 property as a linear RGBA color picker.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColorAttribute : Attribute;

/// <summary>Shows a float or int property as a slider between <see cref="Min"/> and <see cref="Max"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RangeAttribute(float min, float max) : Attribute
{
    public float Min { get; } = min;
    public float Max { get; } = max;
}

/// <summary>Keeps a float or int property at or above <see cref="Value"/> in the inspector.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinAttribute(float value) : Attribute
{
    public float Value { get; } = value;
}

/// <summary>
/// Shows a Vector4 as a color (xyz, linear) plus an offset (w), like Unity's color wheels
/// in Lift Gamma Gain and Shadows Midtones Highlights.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TrackballAttribute : Attribute;

/// <summary>A <c>Guid?</c> property holding the GUID of a texture asset, picked from the project's textures.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TextureAssetAttribute : Attribute;

/// <summary>A <c>Guid?</c> property holding the GUID of a model asset (.obj, .gltf, .glb), picked from the project's models.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ModelAssetAttribute : Attribute;

/// <summary>Short help shown when hovering the property's label.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Groups a component type under a heading in the Add Component menu.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ComponentCategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
