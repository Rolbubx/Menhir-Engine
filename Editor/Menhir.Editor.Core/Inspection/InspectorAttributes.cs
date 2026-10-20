namespace Menhir.Editor.Inspection;

/// <summary>Keeps a public property out of the inspector.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute;

/// <summary>Shows a Vector4 property as a linear RGBA color picker.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColorAttribute : Attribute;
