using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Menhir.Editor.Scripting;

/// <summary>
/// Script field values: their default, how scenes save them (JSON), and how Play mode writes them into a
/// script's struct. The built game converts the saved values the same way (mh_scripts.c).
/// Values are int (Int, and Enum as the option's index), float, bool, Vector2, Vector3, Vector4 (Color) and string.
/// </summary>
public static class ScriptFields
{
    public static object DefaultValue(ScriptField field) => field.Kind switch
    {
        ScriptFieldKind.Int or ScriptFieldKind.Enum => (int)field.Default[0],
        ScriptFieldKind.Float => (float)field.Default[0],
        ScriptFieldKind.Bool => field.Default[0] != 0,
        ScriptFieldKind.Vector2 => new Vector2((float)field.Default[0], (float)field.Default[1]),
        ScriptFieldKind.Vector3 => new Vector3((float)field.Default[0], (float)field.Default[1], (float)field.Default[2]),
        ScriptFieldKind.Color => new Vector4((float)field.Default[0], (float)field.Default[1], (float)field.Default[2], (float)field.Default[3]),
        _ => field.DefaultText,
    };

    /// <summary>The scene's value for the field, or its default when the scene has none or it does not fit.</summary>
    public static object Value(ScriptField field, IReadOnlyDictionary<string, JsonElement> saved) =>
        saved.TryGetValue(field.Name, out JsonElement json) && FromJson(field, json) is { } value ? value : DefaultValue(field);

    /// <summary>A value as scenes save it: enums by option name, vectors and colors as arrays.</summary>
    public static JsonElement ToJson(ScriptField field, object value) => field.Kind switch
    {
        ScriptFieldKind.Enum => JsonSerializer.SerializeToElement(
            value is int i && i >= 0 && i < field.Options.Count ? field.Options[i] : (object)value),
        ScriptFieldKind.Vector2 => JsonSerializer.SerializeToElement(value is Vector2 v ? new[] { v.X, v.Y } : new float[2]),
        ScriptFieldKind.Vector3 => JsonSerializer.SerializeToElement(value is Vector3 v ? new[] { v.X, v.Y, v.Z } : new float[3]),
        ScriptFieldKind.Color => JsonSerializer.SerializeToElement(value is Vector4 v ? new[] { v.X, v.Y, v.Z, v.W } : new float[4]),
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>A saved value converted for the field, or null when it does not fit (the field keeps its default).</summary>
    public static object? FromJson(ScriptField field, JsonElement json)
    {
        switch (field.Kind)
        {
            case ScriptFieldKind.Int:
                return json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out int i) ? i : null;
            case ScriptFieldKind.Float:
                return json.ValueKind == JsonValueKind.Number && json.TryGetSingle(out float f) && float.IsFinite(f) ? f : null;
            case ScriptFieldKind.Bool:
                return json.ValueKind is JsonValueKind.True or JsonValueKind.False ? json.GetBoolean() : null;
            case ScriptFieldKind.String:
                return json.ValueKind == JsonValueKind.String ? json.GetString() : null;
            case ScriptFieldKind.Enum:
                // By name (as the editor saves it), or by index.
                int index = json.ValueKind == JsonValueKind.String ? field.Options.ToList().IndexOf(json.GetString()!)
                    : json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out int n) ? n : -1;
                return index >= 0 && index < field.Options.Count ? index : null;
            default:
                int count = field.Kind == ScriptFieldKind.Vector2 ? 2 : field.Kind == ScriptFieldKind.Vector3 ? 3 : 4;
                if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() != count)
                    return null;
                var v = new float[count];
                int k = 0;
                foreach (JsonElement item in json.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number || !item.TryGetSingle(out v[k]) || !float.IsFinite(v[k]))
                        return null;
                    k++;
                }
                return count == 2 ? new Vector2(v[0], v[1]) : count == 3 ? new Vector3(v[0], v[1], v[2]) : new Vector4(v[0], v[1], v[2], v[3]);
        }
    }

    /// <summary>
    /// Writes a value into a script's struct. A string is copied into memory from <paramref name="allocateString"/>,
    /// which its caller frees when the script goes.
    /// </summary>
    public static unsafe void Write(void* script, ScriptField field, object value, Func<string, nint> allocateString)
    {
        byte* at = (byte*)script + field.Offset;
        switch (value)
        {
            case int i: *(int*)at = i; break;
            case float f: *(float*)at = f; break;
            case bool b: *at = b ? (byte)1 : (byte)0; break;
            case Vector2 v: *(Vector2*)at = v; break;
            case Vector3 v: *(Vector3*)at = v; break;
            case Vector4 v: *(Vector4*)at = v; break;
            case string s: *(nint*)at = allocateString(s); break;
        }
    }

    /// <summary>The value of a field in a script's struct (for tests and debugging).</summary>
    public static unsafe object Read(void* script, ScriptField field)
    {
        byte* at = (byte*)script + field.Offset;
        return field.Kind switch
        {
            ScriptFieldKind.Int or ScriptFieldKind.Enum => *(int*)at,
            ScriptFieldKind.Float => *(float*)at,
            ScriptFieldKind.Bool => *at != 0,
            ScriptFieldKind.Vector2 => *(Vector2*)at,
            ScriptFieldKind.Vector3 => *(Vector3*)at,
            ScriptFieldKind.Color => *(Vector4*)at,
            _ => Marshal.PtrToStringUTF8(*(nint*)at) ?? "",
        };
    }
}
