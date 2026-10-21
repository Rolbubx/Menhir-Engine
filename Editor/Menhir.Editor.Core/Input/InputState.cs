namespace Menhir.Editor.Input;

/// <summary>What the devices report for one frame (the game's mh_input.c gets the same from GLFW).</summary>
public sealed class InputFrame
{
    /// <summary>Codes (see <see cref="InputCodes"/>) of the keys and buttons held down.</summary>
    public HashSet<int> Down { get; } = new();

    /// <summary>Cursor movement since the previous frame, in pixels (y grows downwards).</summary>
    public float MouseDeltaX { get; set; }
    public float MouseDeltaY { get; set; }

    /// <summary>Scroll wheel movement since the previous frame (positive: away from the user).</summary>
    public float Scroll { get; set; }

    /// <summary>Gamepad axes: left X, left Y (down is +1), right X, right Y, left trigger, right trigger (0..1).</summary>
    public float[] GamepadAxes { get; } = new float[6];
}

/// <summary>
/// The Input Manager at run time, like Unity's Input class: <see cref="Update"/> once per frame, then
/// read axes and buttons by name. Key axes move towards ±1 at Sensitivity per second and back to 0 at
/// Gravity; mouse axes are the movement times Sensitivity; gamepad axes are the stick's position past
/// the dead zone. The game's C player (mh_input.c) does the same computation.
/// </summary>
public sealed class InputState
{
    private sealed record Entry(string Name, InputAxisType Type, int Negative, int Positive, int AltNegative,
        int AltPositive, float Gravity, float Dead, float Sensitivity, bool Snap, bool Invert, int Axis);

    private readonly List<Entry> _entries = new();
    private readonly float[] _values;
    private readonly float[] _raw;
    private HashSet<int> _down = new();
    private HashSet<int> _previous = new();

    /// <param name="warnings">Receives the key names that are not known (they never trigger).</param>
    public InputState(IEnumerable<InputAxis> axes, List<string>? warnings = null)
    {
        foreach (InputAxis axis in axes)
            _entries.Add(Compile(axis, warnings));
        _values = new float[_entries.Count];
        _raw = new float[_entries.Count];
    }

    private static Entry Compile(InputAxis a, List<string>? warnings)
    {
        int Code(string name)
        {
            if (InputCodes.TryParse(name, out int code))
                return code;
            warnings?.Add($"Input axis {a.Name}: '{name}' is not a key or button name.");
            return InputCodes.None;
        }

        return new Entry(a.Name, a.Type, Code(a.NegativeButton), Code(a.PositiveButton), Code(a.AltNegativeButton),
            Code(a.AltPositiveButton), MathF.Max(a.Gravity, 0f), MathF.Max(a.Dead, 0f), MathF.Max(a.Sensitivity, 0f),
            a.Snap, a.Invert, Math.Clamp(a.Axis, 0, 5));
    }

    public void Update(InputFrame frame, float deltaTime)
    {
        (_previous, _down) = (_down, _previous);
        _down.Clear();
        _down.UnionWith(frame.Down);

        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            switch (e.Type)
            {
                case InputAxisType.KeyOrMouseButton:
                {
                    bool positive = IsDown(e.Positive) || IsDown(e.AltPositive);
                    bool negative = IsDown(e.Negative) || IsDown(e.AltNegative);
                    float target = (positive ? 1f : 0f) - (negative ? 1f : 0f);
                    float value = _values[i];
                    if (e.Snap && target != 0f && value * target < 0f)
                        value = 0f;
                    value = target != 0f
                        ? MoveTowards(value, target, e.Sensitivity * deltaTime)
                        : MoveTowards(value, 0f, e.Gravity * deltaTime);
                    _values[i] = value;
                    _raw[i] = target;
                    break;
                }
                case InputAxisType.MouseMovement:
                {
                    float movement = e.Axis switch
                    {
                        0 => frame.MouseDeltaX,
                        1 => -frame.MouseDeltaY,
                        _ => frame.Scroll,
                    };
                    _values[i] = _raw[i] = movement * e.Sensitivity;
                    break;
                }
                default:
                {
                    float position = frame.GamepadAxes[e.Axis];
                    if (MathF.Abs(position) < e.Dead)
                        position = 0f;
                    _values[i] = _raw[i] = position * e.Sensitivity;
                    break;
                }
            }
        }
    }

    public float GetAxis(string name) => Largest(name, _values, applyDeadZone: true);

    /// <summary>Without smoothing: -1, 0 or 1 for keys.</summary>
    public float GetAxisRaw(string name) => Largest(name, _raw, applyDeadZone: false);

    /// <summary>A positive button of the axis is held.</summary>
    public bool GetButton(string name) => AnyPositive(name, _down);

    /// <summary>A positive button of the axis went down this frame.</summary>
    public bool GetButtonDown(string name) => AnyPositive(name, _down) && !AnyPositive(name, _previous);

    /// <summary>The positive buttons of the axis were all released this frame.</summary>
    public bool GetButtonUp(string name) => !AnyPositive(name, _down) && AnyPositive(name, _previous);

    public bool GetKey(int code) => IsDown(code);

    private bool IsDown(int code) => code != InputCodes.None && _down.Contains(code);

    private bool AnyPositive(string name, HashSet<int> set)
    {
        foreach (Entry e in _entries)
        {
            if (e.Name == name && e.Type == InputAxisType.KeyOrMouseButton &&
                ((e.Positive != InputCodes.None && set.Contains(e.Positive)) ||
                 (e.AltPositive != InputCodes.None && set.Contains(e.AltPositive))))
                return true;
        }
        return false;
    }

    private float Largest(string name, float[] values, bool applyDeadZone)
    {
        float best = 0f;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            if (e.Name != name)
                continue;
            float v = values[i];
            if (applyDeadZone && e.Type == InputAxisType.KeyOrMouseButton && MathF.Abs(v) < e.Dead)
                v = 0f;
            if (e.Invert)
                v = -v;
            if (MathF.Abs(v) > MathF.Abs(best))
                best = v;
        }
        return best;
    }

    public static float MoveTowards(float current, float target, float maxDelta) =>
        MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;
}
