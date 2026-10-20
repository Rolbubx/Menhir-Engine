using System.Numerics;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Tests.Mathematics;

public class EulerAnglesTests
{
    private const float Tolerance = 1e-3f;

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(0, 45, 0)]
    [InlineData(0, 0, -60)]
    [InlineData(10, 20, 30)]
    [InlineData(-45, 170, -120)]
    [InlineData(89, -90, 15)]
    public void FromQuaternion_RoundTripsAngles(float x, float y, float z)
    {
        var angles = new Vector3(x, y, z);

        Vector3 result = EulerAngles.FromQuaternion(EulerAngles.ToQuaternion(angles));

        Assert.Equal(x, result.X, Tolerance);
        Assert.Equal(y, result.Y, Tolerance);
        Assert.Equal(z, result.Z, Tolerance);
    }

    [Fact]
    public void ToQuaternion_YawTurnsAroundUp()
    {
        Quaternion q = EulerAngles.ToQuaternion(new Vector3(0, 90, 0));

        // Right-handed, Y up: a positive yaw turns -Z (forward) towards -X.
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, q);

        AssertClose(-Vector3.UnitX, forward);
    }

    [Fact]
    public void ToQuaternion_AppliesRollThenPitchThenYaw()
    {
        var angles = new Vector3(20, 40, 60);
        // a * b applies b first.
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Radians(40)) *
                              Quaternion.CreateFromAxisAngle(Vector3.UnitX, Radians(20)) *
                              Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Radians(60));

        Quaternion q = EulerAngles.ToQuaternion(angles);

        AssertClose(Vector3.Transform(Vector3.One, expected), Vector3.Transform(Vector3.One, q));
    }

    [Theory]
    [InlineData(90, 30, 10)]
    [InlineData(-90, -50, 70)]
    public void FromQuaternion_AtGimbalLock_GivesSameRotation(float x, float y, float z)
    {
        Quaternion q = EulerAngles.ToQuaternion(new Vector3(x, y, z));

        Vector3 result = EulerAngles.FromQuaternion(q);
        Quaternion back = EulerAngles.ToQuaternion(result);

        Assert.Equal(x, result.X, Tolerance);
        AssertClose(Vector3.Transform(new Vector3(1, 2, 3), q), Vector3.Transform(new Vector3(1, 2, 3), back));
    }

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");
    }
}
