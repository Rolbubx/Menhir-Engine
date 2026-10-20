using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Menhir.Editor.Serialization;

/// <summary>
/// Writes vectors and quaternions as one-line number arrays ("[0, 0.5, 0]") so that
/// scene files stay short and easy to read, even when indented.
/// </summary>
public abstract class FloatArrayConverter<T> : JsonConverter<T>
{
    protected abstract int Length { get; }

    protected abstract void ToArray(T value, Span<float> values);

    protected abstract T FromArray(ReadOnlySpan<float> values);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected an array of {Length} numbers for {typeof(T).Name}.");

        Span<float> values = stackalloc float[Length];
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || count == Length)
                throw new JsonException($"Expected an array of {Length} numbers for {typeof(T).Name}.");
            values[count++] = reader.GetSingle();
        }
        if (count != Length)
            throw new JsonException($"Expected an array of {Length} numbers for {typeof(T).Name}.");
        return FromArray(values);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        Span<float> values = stackalloc float[Length];
        ToArray(value, values);

        var parts = new string[Length];
        for (int i = 0; i < Length; i++)
        {
            if (!float.IsFinite(values[i]))
                throw new JsonException($"Cannot save a {typeof(T).Name} containing {values[i]}.");
            parts[i] = values[i].ToString(CultureInfo.InvariantCulture); // shortest form that reads back exactly
        }
        writer.WriteRawValue($"[{string.Join(", ", parts)}]");
    }
}

public sealed class Vector2Converter : FloatArrayConverter<Vector2>
{
    protected override int Length => 2;
    protected override void ToArray(Vector2 v, Span<float> a) { a[0] = v.X; a[1] = v.Y; }
    protected override Vector2 FromArray(ReadOnlySpan<float> a) => new(a[0], a[1]);
}

public sealed class Vector3Converter : FloatArrayConverter<Vector3>
{
    protected override int Length => 3;
    protected override void ToArray(Vector3 v, Span<float> a) { a[0] = v.X; a[1] = v.Y; a[2] = v.Z; }
    protected override Vector3 FromArray(ReadOnlySpan<float> a) => new(a[0], a[1], a[2]);
}

public sealed class Vector4Converter : FloatArrayConverter<Vector4>
{
    protected override int Length => 4;
    protected override void ToArray(Vector4 v, Span<float> a) { a[0] = v.X; a[1] = v.Y; a[2] = v.Z; a[3] = v.W; }
    protected override Vector4 FromArray(ReadOnlySpan<float> a) => new(a[0], a[1], a[2], a[3]);
}

/// <summary>Quaternions are written as [x, y, z, w].</summary>
public sealed class QuaternionConverter : FloatArrayConverter<Quaternion>
{
    protected override int Length => 4;
    protected override void ToArray(Quaternion q, Span<float> a) { a[0] = q.X; a[1] = q.Y; a[2] = q.Z; a[3] = q.W; }
    protected override Quaternion FromArray(ReadOnlySpan<float> a) => new(a[0], a[1], a[2], a[3]);
}
