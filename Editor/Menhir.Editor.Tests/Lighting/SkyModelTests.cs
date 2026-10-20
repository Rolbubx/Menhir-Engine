using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Lighting;

public class SkyModelTests
{
    private static readonly ProceduralSkybox Sky = new();
    private static readonly Vector3 Noon = Vector3.UnitY;

    private static Vector3 Dir(float x, float y, float z) => Vector3.Normalize(new Vector3(x, y, z));

    [Fact]
    public void DaySky_IsBlueOverhead()
    {
        Vector3 zenith = SkyModel.Evaluate(Sky, Vector3.UnitY, Dir(0.3f, 1, 0));
        Assert.True(zenith.Z > zenith.X, $"zenith {zenith} should be more blue than red");
        Assert.True(zenith.Z > 0.05f);
    }

    [Fact]
    public void Sunset_IsRedderTowardsTheSunThanOverhead()
    {
        Vector3 toSun = Dir(1, 0.02f, 0);
        Vector3 horizon = SkyModel.Evaluate(Sky, Dir(1, 0.05f, 0), toSun);
        Vector3 zenith = SkyModel.Evaluate(Sky, Vector3.UnitY, toSun);
        Assert.True(horizon.X / horizon.Z > zenith.X / zenith.Z);
    }

    [Fact]
    public void Night_IsDarkerThanDay()
    {
        float day = SkyModel.Evaluate(Sky, Dir(0, 0.5f, 1), Noon).Length();
        float night = SkyModel.Evaluate(Sky, Dir(0, 0.5f, 1), Dir(0, -0.6f, 1)).Length();
        Assert.True(night < day * 0.5f);
    }

    [Fact]
    public void Exposure_ScalesTheSky()
    {
        Vector3 normal = SkyModel.Evaluate(Sky, Dir(0, 0.3f, 1), Noon);
        Vector3 doubled = SkyModel.Evaluate(new ProceduralSkybox { Exposure = Sky.Exposure * 2 }, Dir(0, 0.3f, 1), Noon);
        Assert.Equal(normal.X * 2, doubled.X, 3);
    }

    [Fact]
    public void Ground_FollowsTheGroundColor()
    {
        var green = new ProceduralSkybox { GroundColor = new Vector4(0, 1, 0, 1) };
        Vector3 ground = SkyModel.Evaluate(green, -Vector3.UnitY, Noon);
        Assert.True(ground.Y > ground.X && ground.Y > ground.Z);
    }

    [Fact]
    public void Ambient_IsBrighterFromAboveAndScalesWithIntensity()
    {
        var (top, _, bottom) = SkyModel.Ambient(Sky, Noon);
        Assert.True(top.Length() > bottom.Length());

        var (dimTop, _, _) = SkyModel.Ambient(new ProceduralSkybox { AmbientIntensity = 0.5f }, Noon);
        Assert.Equal(top.Y * 0.5f, dimTop.Y, 4);
    }
}
