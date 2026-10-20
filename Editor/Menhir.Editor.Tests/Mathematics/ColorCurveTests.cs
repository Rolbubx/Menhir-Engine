using System.Numerics;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Tests.Mathematics;

public class ColorCurveTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void Identity_ReturnsX(float x) => Assert.Equal(x, ColorCurve.Identity().Evaluate(x), 4);

    [Fact]
    public void Flat_IsHalfEverywhere()
    {
        ColorCurve curve = ColorCurve.Flat(loop: true);
        Assert.Equal(0.5f, curve.Evaluate(0.2f));
        Assert.Equal(0.5f, curve.Evaluate(0.9f));
    }

    [Fact]
    public void Evaluate_PassesThroughPointsWithoutOvershoot()
    {
        ColorCurve curve = ColorCurve.Identity().WithPoints([new(0, 0), new(0.5f, 0.9f), new(0.6f, 1f), new(1, 1)]);

        Assert.Equal(0.9f, curve.Evaluate(0.5f), 4);
        for (float x = 0; x <= 1f; x += 0.01f)
            Assert.InRange(curve.Evaluate(x), 0f, 1.0001f);
    }

    [Fact]
    public void Evaluate_ClampsOutsideThePoints()
    {
        ColorCurve curve = ColorCurve.Identity().WithPoints([new(0.2f, 0.3f), new(0.8f, 0.6f)]);
        Assert.Equal(0.3f, curve.Evaluate(0f));
        Assert.Equal(0.6f, curve.Evaluate(1f));
    }

    [Fact]
    public void Loop_JoinsUpAcrossZeroAndOne()
    {
        ColorCurve curve = ColorCurve.Flat(loop: true).WithPoints([new(0.25f, 0.2f), new(0.75f, 0.8f)]);

        Assert.Equal(curve.Evaluate(0f), curve.Evaluate(1f), 4);
        Assert.Equal(curve.Evaluate(0.1f), curve.Evaluate(1.1f), 4);
    }

    [Fact]
    public void WithPoints_SortsAndClampsAndKeepsTheOriginal()
    {
        ColorCurve original = ColorCurve.Identity();
        ColorCurve edited = original.WithPoints([new(1.5f, 1f), new(0.5f, -1f)]);

        Assert.Equal([new Vector2(0.5f, 0f), new Vector2(1f, 1f)], edited.Points);
        Assert.Equal(2, original.Points.Count);
        Assert.Equal(new Vector2(0, 0), original.Points[0]);
    }

    [Fact]
    public void Bake_SamplesEvenly()
    {
        float[] values = ColorCurve.Identity().Bake(5);
        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 1f], values.Select(v => MathF.Round(v, 4)));
    }
}
