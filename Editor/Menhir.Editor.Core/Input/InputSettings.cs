using Menhir.Editor.Inspection;

namespace Menhir.Editor.Input;

public enum InputAxisType
{
    KeyOrMouseButton,
    MouseMovement,
    JoystickAxis,
}

/// <summary>
/// One entry of the Input Manager, like Unity's: a named axis or button driven by keys and buttons,
/// mouse movement or a gamepad axis. Several entries can share a name (e.g. "Horizontal" for the
/// keyboard and for the gamepad); the one with the largest value wins.
/// </summary>
public sealed class InputAxis
{
    public string Name { get; set; } = "New Axis";

    public InputAxisType Type { get; set; } = InputAxisType.KeyOrMouseButton;

    [Tooltip("Key or button names, e.g. \"a\", \"left shift\", \"mouse 0\", \"joystick button 0\" (see Docs/Input.md).")]
    public string NegativeButton { get; set; } = "";

    public string PositiveButton { get; set; } = "";

    public string AltNegativeButton { get; set; } = "";

    public string AltPositiveButton { get; set; } = "";

    [Min(0)]
    [Tooltip("Keys: speed (units per second) at which the value falls back to 0.")]
    public float Gravity { get; set; } = 3f;

    [Min(0)]
    [Tooltip("Values closer to 0 than this count as 0.")]
    public float Dead { get; set; } = 0.001f;

    [Min(0)]
    [Tooltip("Keys: speed towards ±1. Mouse and gamepad: multiplier of the movement.")]
    public float Sensitivity { get; set; } = 3f;

    [Tooltip("Keys: pressing the opposite direction jumps through 0 at once.")]
    public bool Snap { get; set; } = true;

    public bool Invert { get; set; }

    [Range(0, 5)]
    [Tooltip("Mouse: 0 = X, 1 = Y, 2 = scroll wheel. Gamepad: 0 left X, 1 left Y, 2 right X, 3 right Y, 4 left trigger, 5 right trigger.")]
    public int Axis { get; set; }

    public InputAxis Clone() => (InputAxis)MemberwiseClone();
}

/// <summary>The project's Input Manager (saved in ProjectSettings.json), with Unity's default entries.</summary>
public sealed class InputSettings
{
    public List<InputAxis> Axes { get; set; } = Defaults();

    public static List<InputAxis> Defaults() =>
    [
        Keys("Horizontal", "left", "right", "a", "d"),
        Keys("Vertical", "down", "up", "s", "w"),
        Button("Fire1", "left ctrl", "mouse 0"),
        Button("Fire2", "left alt", "mouse 1"),
        Button("Fire3", "left shift", "mouse 2"),
        Button("Jump", "space"),
        Mouse("Mouse X", 0),
        Mouse("Mouse Y", 1),
        Mouse("Mouse ScrollWheel", 2),
        Joystick("Horizontal", 0, invert: false),
        Joystick("Vertical", 1, invert: true),
        Button("Fire1", "joystick button 0"),
        Button("Fire2", "joystick button 1"),
        Button("Fire3", "joystick button 2"),
        Button("Jump", "joystick button 3"),
        Button("Submit", "return", "joystick button 0"),
        Button("Cancel", "escape", "joystick button 1"),
    ];

    private static InputAxis Keys(string name, string negative, string positive, string altNegative, string altPositive) => new()
    {
        Name = name, NegativeButton = negative, PositiveButton = positive,
        AltNegativeButton = altNegative, AltPositiveButton = altPositive,
    };

    private static InputAxis Button(string name, string positive, string altPositive = "") => new()
    {
        Name = name, PositiveButton = positive, AltPositiveButton = altPositive,
        Gravity = 1000f, Sensitivity = 1000f, Snap = false,
    };

    private static InputAxis Mouse(string name, int axis) => new()
    {
        Name = name, Type = InputAxisType.MouseMovement, Axis = axis, Gravity = 0f, Dead = 0f, Sensitivity = 0.1f, Snap = false,
    };

    private static InputAxis Joystick(string name, int axis, bool invert) => new()
    {
        Name = name, Type = InputAxisType.JoystickAxis, Axis = axis, Invert = invert,
        Gravity = 0f, Dead = 0.19f, Sensitivity = 1f, Snap = false,
    };
}
