using System.Numerics;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Tests.Mathematics;

public class ColorSpaceTests
{
    [Fact]
    public void GammaToLinear_DarkensMidtonesAndKeepsAlpha()
    {
        Vector4 linear = ColorSpace.GammaToLinear(new Vector4(0.5f, 0, 1, 0.25f));

        Assert.Equal(0.2176f, linear.X, 1e-3f);
        Assert.Equal(0f, linear.Y);
        Assert.Equal(1f, linear.Z, 1e-6f);
        Assert.Equal(0.25f, linear.W);
    }

    [Fact]
    public void Conversions_RoundTrip()
    {
        var color = new Vector4(0.1f, 0.4f, 0.9f, 1f);

        Vector4 result = ColorSpace.GammaToLinear(ColorSpace.LinearToGamma(color));

        Assert.True(Vector4.Distance(color, result) < 1e-5f);
    }
}
