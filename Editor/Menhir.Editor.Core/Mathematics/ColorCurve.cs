using System.Numerics;

namespace Menhir.Editor.Mathematics;

/// <summary>
/// A curve through control points (x and y in 0..1), smooth and without overshoot
/// (monotone cubic Hermite). Used by Color Curves. Edits replace the whole curve
/// (see <see cref="WithPoints"/>) so undo can keep the previous one.
/// </summary>
public sealed class ColorCurve
{
    /// <summary>Control points sorted by X.</summary>
    public List<Vector2> Points { get; set; } = [];

    /// <summary>Wraps around at 0 and 1 (hue curves).</summary>
    public bool Loop { get; set; }

    /// <summary>The value of a curve without points.</summary>
    public float DefaultValue { get; set; } = 0.5f;

    /// <summary>y = x, the neutral master and channel curves.</summary>
    public static ColorCurve Identity() => new() { Points = [new(0, 0), new(1, 1)] };

    /// <summary>A horizontal line at 0.5, the neutral hue and saturation curves.</summary>
    public static ColorCurve Flat(bool loop = false) => new() { Loop = loop, DefaultValue = 0.5f };

    public ColorCurve WithPoints(IEnumerable<Vector2> points) => new()
    {
        Points = points.Select(p => Vector2.Clamp(p, Vector2.Zero, Vector2.One)).OrderBy(p => p.X).ToList(),
        Loop = Loop,
        DefaultValue = DefaultValue,
    };

    public float Evaluate(float x)
    {
        List<Vector2> points = Points;
        if (points.Count == 0)
            return DefaultValue;
        if (points.Count == 1)
            return points[0].Y;

        if (Loop)
        {
            // Repeat the points one period on each side, so the curve joins up across 0 / 1.
            x -= MathF.Floor(x);
            var extended = new List<Vector2>(points.Count + 2) { points[^1] - Vector2.UnitX };
            extended.AddRange(points);
            extended.Add(points[0] + Vector2.UnitX);
            points = extended;
        }
        else
        {
            if (x <= points[0].X)
                return points[0].Y;
            if (x >= points[^1].X)
                return points[^1].Y;
        }

        int i = 0;
        while (i < points.Count - 2 && x > points[i + 1].X)
            i++;
        return Hermite(points, i, x);
    }

    /// <summary>Evaluates the curve at <paramref name="count"/> evenly spaced x from 0 to 1.</summary>
    public float[] Bake(int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = Evaluate(Loop ? i / (float)count : i / (float)(count - 1));
        return values;
    }

    private static float Hermite(List<Vector2> p, int i, float x)
    {
        Vector2 a = p[i], b = p[i + 1];
        float h = b.X - a.X;
        if (h <= 1e-6f)
            return b.Y;
        float t = (x - a.X) / h;
        float m0 = Tangent(p, i) * h, m1 = Tangent(p, i + 1) * h;
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * a.Y + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * b.Y + (t3 - t2) * m1;
    }

    /// <summary>Fritsch-Carlson tangent: 0 at local extremes, so the curve never overshoots its points.</summary>
    private static float Tangent(List<Vector2> p, int i)
    {
        float Slope(int k) => (p[k + 1].Y - p[k].Y) / MathF.Max(p[k + 1].X - p[k].X, 1e-6f);
        if (i == 0)
            return Slope(0);
        if (i == p.Count - 1)
            return Slope(i - 1);
        float s0 = Slope(i - 1), s1 = Slope(i);
        if (s0 * s1 <= 0)
            return 0;
        return 2 / (1 / s0 + 1 / s1); // harmonic mean
    }
}
