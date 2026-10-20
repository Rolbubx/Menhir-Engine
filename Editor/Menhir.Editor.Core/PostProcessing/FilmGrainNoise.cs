namespace Menhir.Editor.PostProcessing;

/// <summary>
/// Tileable grain textures, one per <see cref="FilmGrainType"/>: random noise blurred more for the
/// coarser types (Unity ships images; these are generated so the look stays in the same spirit).
/// Values are centered on 128 (no change).
/// </summary>
public static class FilmGrainNoise
{
    public const int Size = 256;

    public static byte[] Generate(FilmGrainType type)
    {
        (float radius, float contrast) = type switch
        {
            FilmGrainType.Thin1 => (0f, 1.0f),
            FilmGrainType.Thin2 => (0.5f, 1.1f),
            FilmGrainType.Medium1 => (0.8f, 1.2f),
            FilmGrainType.Medium2 => (1.0f, 1.3f),
            FilmGrainType.Medium3 => (1.2f, 1.4f),
            FilmGrainType.Medium4 => (1.4f, 1.5f),
            FilmGrainType.Medium5 => (1.7f, 1.6f),
            FilmGrainType.Medium6 => (2.0f, 1.7f),
            FilmGrainType.Large01 => (2.6f, 1.9f),
            _ => (3.2f, 2.1f),
        };

        var random = new Random(1234 + (int)type);
        var values = new float[Size * Size];
        for (int i = 0; i < values.Length; i++)
            values[i] = (float)random.NextDouble() - 0.5f;

        if (radius > 0f)
            values = Blur(values, radius);

        // Faint, like Unity's grain textures: a standard deviation of a few percent around mid-gray.
        float variance = values.Sum(v => v * v) / values.Length;
        float scale = contrast * 0.02f / MathF.Sqrt(MathF.Max(variance, 1e-8f));
        var bytes = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
            bytes[i] = (byte)Math.Clamp(MathF.Round(128f + values[i] * scale * 255f), 0f, 255f);
        return bytes;
    }

    /// <summary>Separable Gaussian blur that wraps at the edges (so the texture tiles).</summary>
    private static float[] Blur(float[] source, float sigma)
    {
        int radius = (int)MathF.Ceiling(sigma * 2.5f);
        var weights = new float[radius * 2 + 1];
        float total = 0f;
        for (int i = -radius; i <= radius; i++)
            total += weights[i + radius] = MathF.Exp(-i * i / (2f * sigma * sigma));
        for (int i = 0; i < weights.Length; i++)
            weights[i] /= total;

        var horizontal = new float[source.Length];
        var result = new float[source.Length];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum += source[y * Size + (x + k + Size) % Size] * weights[k + radius];
            horizontal[y * Size + x] = sum;
        }
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum += horizontal[(y + k + Size) % Size * Size + x] * weights[k + radius];
            result[y * Size + x] = sum;
        }
        return result;
    }
}
