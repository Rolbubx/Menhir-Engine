namespace Menhir.Editor.Serialization;

/// <summary>A scene file that cannot be loaded (invalid JSON, missing fields, newer version...).</summary>
public sealed class SceneFormatException(string message, Exception? inner = null) : Exception(message, inner);
