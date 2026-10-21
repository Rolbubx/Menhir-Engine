using System.Reflection;
using System.Text.Json;
using MenhirEngine;

namespace Menhir.Editor.Scripting;

/// <summary>The kinds of script fields the inspector edits and scenes save.</summary>
public enum ScriptFieldKind
{
    Int,
    Long,
    Float,
    Double,
    Bool,
    String,
    Enum,
    Vector2,
    Vector3,
}

/// <summary>A public field of a script class shown in the inspector.</summary>
public sealed record ScriptField(FieldInfo Field, ScriptFieldKind Kind, float? Min, float? Max, string? Tooltip)
{
    public string Name => Field.Name;
}

/// <summary>Reads script fields and converts their values to and from the scene's JSON.</summary>
public static class ScriptFields
{
    public static IReadOnlyList<ScriptField> Of(Type scriptType)
    {
        var fields = new List<ScriptField>();
        foreach (FieldInfo field in scriptType.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
        {
            if (field.IsInitOnly || field.DeclaringType == typeof(MenhirEngine.Script) ||
                field.GetCustomAttribute<HideInInspectorAttribute>() != null || KindOf(field.FieldType) is not { } kind)
                continue;
            var range = field.GetCustomAttribute<MenhirEngine.RangeAttribute>();
            fields.Add(new ScriptField(field, kind, range?.min, range?.max, field.GetCustomAttribute<MenhirEngine.TooltipAttribute>()?.tooltip));
        }
        return fields;
    }

    public static ScriptFieldKind? KindOf(Type type)
    {
        if (type.IsEnum) return ScriptFieldKind.Enum;
        if (type == typeof(int)) return ScriptFieldKind.Int;
        if (type == typeof(long)) return ScriptFieldKind.Long;
        if (type == typeof(float)) return ScriptFieldKind.Float;
        if (type == typeof(double)) return ScriptFieldKind.Double;
        if (type == typeof(bool)) return ScriptFieldKind.Bool;
        if (type == typeof(string)) return ScriptFieldKind.String;
        if (type == typeof(Vector2)) return ScriptFieldKind.Vector2;
        if (type == typeof(Vector3)) return ScriptFieldKind.Vector3;
        return null;
    }

    /// <summary>A field's value as saved in scenes (enums by name, vectors as arrays).</summary>
    public static JsonElement ToJson(ScriptFieldKind kind, object? value) => kind switch
    {
        ScriptFieldKind.Enum => JsonSerializer.SerializeToElement(value?.ToString() ?? ""),
        ScriptFieldKind.Vector2 => JsonSerializer.SerializeToElement(value is Vector2 v ? new[] { v.x, v.y } : new float[2]),
        ScriptFieldKind.Vector3 => JsonSerializer.SerializeToElement(value is Vector3 v ? new[] { v.x, v.y, v.z } : new float[3]),
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>A saved value converted for a field, or null when it does not fit (the field keeps its default).</summary>
    public static object? FromJson(ScriptField field, JsonElement json)
    {
        try
        {
            Type type = field.Field.FieldType;
            switch (field.Kind)
            {
                case ScriptFieldKind.Enum:
                    return json.ValueKind == JsonValueKind.String && Enum.TryParse(type, json.GetString(), out object? e) ? e
                        : json.ValueKind == JsonValueKind.Number ? Enum.ToObject(type, json.GetInt64()) : null;
                case ScriptFieldKind.Vector2:
                {
                    float[] a = json.Deserialize<float[]>()!;
                    return a.Length == 2 ? new Vector2(a[0], a[1]) : null;
                }
                case ScriptFieldKind.Vector3:
                {
                    float[] a = json.Deserialize<float[]>()!;
                    return a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : null;
                }
                default:
                    return json.Deserialize(type);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sets the saved values on a script instance; returns the names of values that did not fit.</summary>
    public static List<string> Apply(object script, IReadOnlyDictionary<string, JsonElement> values)
    {
        var problems = new List<string>();
        foreach (ScriptField field in Of(script.GetType()))
        {
            if (!values.TryGetValue(field.Name, out JsonElement json))
                continue;
            if (FromJson(field, json) is { } value)
                field.Field.SetValue(script, value);
            else
                problems.Add(field.Name);
        }
        return problems;
    }
}
