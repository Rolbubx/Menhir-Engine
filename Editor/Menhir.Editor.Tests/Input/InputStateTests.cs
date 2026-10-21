using Menhir.Editor.Input;

namespace Menhir.Editor.Tests.Input;

public class InputStateTests
{
    private static int Code(string name) => InputCodes.TryParse(name, out int code) ? code : throw new ArgumentException(name);

    private static InputFrame Frame(params string[] down)
    {
        var frame = new InputFrame();
        foreach (string name in down)
            frame.Down.Add(Code(name));
        return frame;
    }

    [Fact]
    public void Codes_FollowUnityNames()
    {
        Assert.Equal(65, Code("a"));
        Assert.Equal(65, Code(" A "));
        Assert.Equal(32, Code("space"));
        Assert.Equal(340, Code("left shift"));
        Assert.Equal(1001, Code("mouse 1"));
        Assert.Equal(2003, Code("joystick button 3"));
        Assert.True(InputCodes.TryParse("", out int none) && none == InputCodes.None);
        Assert.False(InputCodes.TryParse("hyperspace", out _));
    }

    [Fact]
    public void KeyAxis_RisesWithSensitivity_AndFallsWithGravity()
    {
        var input = new InputState(InputSettings.Defaults());
        input.Update(Frame("d"), 0.1f);
        Assert.Equal(0.3f, input.GetAxis("Horizontal"), 5);
        Assert.Equal(1f, input.GetAxisRaw("Horizontal"));
        input.Update(Frame("right"), 0.5f);
        Assert.Equal(1f, input.GetAxis("Horizontal"));
        input.Update(Frame(), 0.1f);
        Assert.Equal(0.7f, input.GetAxis("Horizontal"), 5);
        Assert.Equal(0f, input.GetAxisRaw("Horizontal"));
    }

    [Fact]
    public void KeyAxis_SnapsThroughZeroOnTheOppositeKey()
    {
        var input = new InputState(InputSettings.Defaults());
        input.Update(Frame("w"), 1f);
        input.Update(Frame("s"), 0.1f);
        Assert.Equal(-0.3f, input.GetAxis("Vertical"), 5);
    }

    [Fact]
    public void Buttons_ReportDownAndUpOnce()
    {
        var input = new InputState(InputSettings.Defaults());
        input.Update(Frame("mouse 0"), 0.016f);
        Assert.True(input.GetButton("Fire1") && input.GetButtonDown("Fire1"));
        input.Update(Frame("mouse 0", "left ctrl"), 0.016f);
        Assert.True(input.GetButton("Fire1"));
        Assert.False(input.GetButtonDown("Fire1"));
        input.Update(Frame(), 0.016f);
        Assert.True(input.GetButtonUp("Fire1"));
        Assert.False(input.GetButton("Fire1"));
        input.Update(Frame("joystick button 0"), 0.016f); // the gamepad's Fire1 entry
        Assert.True(input.GetButtonDown("Fire1"));
        Assert.True(input.GetButtonDown("Submit"));
    }

    [Fact]
    public void Mouse_IsTheMovementTimesSensitivity_UpIsPositive()
    {
        var input = new InputState(InputSettings.Defaults());
        var frame = new InputFrame { MouseDeltaX = 10, MouseDeltaY = -20, Scroll = 1 };
        input.Update(frame, 0.016f);
        Assert.Equal(1f, input.GetAxis("Mouse X"), 5);
        Assert.Equal(2f, input.GetAxis("Mouse Y"), 5);
        Assert.Equal(0.1f, input.GetAxis("Mouse ScrollWheel"), 5);
        input.Update(new InputFrame(), 0.016f);
        Assert.Equal(0f, input.GetAxis("Mouse X"));
    }

    [Fact]
    public void Gamepad_HasADeadZone_AndTheLargestEntryWins()
    {
        var input = new InputState(InputSettings.Defaults());
        var frame = new InputFrame();
        frame.GamepadAxes[0] = 0.1f;   // inside the dead zone
        frame.GamepadAxes[1] = -0.8f;  // stick up (GLFW: up is negative); Vertical inverts it
        input.Update(frame, 0.016f);
        Assert.Equal(0f, input.GetAxis("Horizontal"));
        Assert.Equal(0.8f, input.GetAxis("Vertical"), 5);

        frame.Down.Add(Code("s"));     // the keyboard entry is smaller until it ramps up
        input.Update(frame, 0.1f);
        Assert.Equal(0.8f, input.GetAxis("Vertical"), 5);
        input.Update(frame, 1f);
        Assert.Equal(-1f, input.GetAxis("Vertical"), 5);
    }

    [Fact]
    public void UnknownKeyNames_AreReported()
    {
        var warnings = new List<string>();
        _ = new InputState([new InputAxis { Name = "Dash", PositiveButton = "hyperspace" }], warnings);
        Assert.Contains(warnings, w => w.Contains("hyperspace"));
    }
}
