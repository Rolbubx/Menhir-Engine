using System.Numerics;
using Menhir.Editor.PostProcessing;

namespace Menhir.Editor.Tests.PostProcessing;

public class PostProcessMathTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 0.01f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void WhiteBalance_NeutralIsAboutOne() =>
        AssertNear(Vector3.One, PostProcessMath.WhiteBalanceCoefficients(0, 0), 0.02f);

    [Fact]
    public void WhiteBalance_WarmerBoostsLongWavelengths()
    {
        Vector3 warm = PostProcessMath.WhiteBalanceCoefficients(50, 0);
        Vector3 cool = PostProcessMath.WhiteBalanceCoefficients(-50, 0);
        Assert.True(warm.X / warm.Z > cool.X / cool.Z);
    }

    [Fact]
    public void LiftGammaGain_NeutralWheelsChangeNothing()
    {
        var neutral = new Vector4(1, 1, 1, 0);
        var (lift, gamma, gain) = PostProcessMath.LiftGammaGain(neutral, neutral, neutral);
        AssertNear(Vector3.Zero, lift);
        AssertNear(Vector3.One, gamma);
        AssertNear(Vector3.One, gain);
    }

    [Fact]
    public void LiftGammaGain_OffsetMovesEveryChannel()
    {
        var (lift, _, gain) = PostProcessMath.LiftGammaGain(new Vector4(1, 1, 1, 0.2f), new Vector4(1, 1, 1, 0), new Vector4(1, 1, 1, -0.5f));
        AssertNear(new Vector3(0.2f), lift);
        AssertNear(new Vector3(0.5f), gain);
    }

    [Fact]
    public void ShadowsMidtonesHighlights_NeutralIsOne()
    {
        var neutral = new Vector4(1, 1, 1, 0);
        var (s, m, h) = PostProcessMath.ShadowsMidtonesHighlights(neutral, neutral, neutral);
        AssertNear(Vector3.One, s);
        AssertNear(Vector3.One, m);
        AssertNear(Vector3.One, h);
    }

    [Fact]
    public void BloomTint_KeepsTheLuminance()
    {
        AssertNear(Vector3.One, PostProcessMath.BloomTint(Vector4.One));
        Assert.Equal(1f, PostProcessMath.Luminance(PostProcessMath.BloomTint(new Vector4(1, 0.5f, 0.1f, 1))), 4);
    }

    [Fact]
    public void BokehKernel_Has42PointsInTheUnitDisk()
    {
        Vector2[] kernel = PostProcessMath.BokehKernel(5, 0.5f, 30);
        Assert.Equal(42, kernel.Length);
        Assert.All(kernel, p => Assert.InRange(p.Length(), 0.05f, 1.0001f));
    }

    [Fact]
    public void Panini_ZeroDistanceKeepsTheImage()
    {
        Vector4 p = PostProcessMath.Panini(new PaniniProjection { Distance = 0 }, MathF.PI / 3, 16f / 9f);
        Assert.Equal(1f, p.W, 4);
        Assert.Equal(MathF.Tan(MathF.PI / 6), p.Y, 4);
    }

    [Fact]
    public void Panini_CropToFitZoomsIn()
    {
        Vector4 p = PostProcessMath.Panini(new PaniniProjection { Distance = 1, CropToFit = 1 }, MathF.PI / 2, 16f / 9f);
        Assert.InRange(p.W, 0.1f, 0.999f);
    }

    [Fact]
    public void LensCoefficient_GrowsAsTheApertureOpens()
    {
        float narrow = PostProcessMath.LensCoefficient(50, 16, 10);
        float wide = PostProcessMath.LensCoefficient(50, 1.4f, 10);
        Assert.True(wide > narrow * 10);
    }

    [Fact]
    public void LensDistortion_NeutralCenterIsZero()
    {
        var (center, parameters) = PostProcessMath.LensDistortion(new LensDistortion { Intensity = 0.5f });
        Assert.Equal(0f, center.X, 5);
        Assert.Equal(50f, parameters.W, 4);
    }
}
