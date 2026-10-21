namespace Menhir.Editor.Input;

/// <summary>
/// Key and button names (Unity's) and the codes the game uses for them: GLFW key codes for keys,
/// 1000 + n for mouse button n, 2000 + n for gamepad button n (GLFW's gamepad layout: 0 A, 1 B,
/// 2 X, 3 Y, 4 left bumper, 5 right bumper, 6 back, 7 start, 8 guide, 9 left stick, 10 right stick,
/// 11 to 14 the d-pad up, right, down, left).
/// </summary>
public static class InputCodes
{
    public const int None = -1;
    public const int FirstMouseButton = 1000;
    public const int FirstGamepadButton = 2000;
    public const int MouseButtons = 8;
    public const int GamepadButtons = 15;

    private static readonly Dictionary<string, int> Codes = Build();

    public static IReadOnlyCollection<string> Names => Codes.Keys;

    private static Dictionary<string, int> Build()
    {
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (char c = 'a'; c <= 'z'; c++)
            codes[c.ToString()] = 65 + (c - 'a');
        for (int d = 0; d <= 9; d++)
        {
            codes[d.ToString()] = 48 + d;
            codes[$"[{d}]"] = 320 + d; // keypad
        }
        for (int f = 1; f <= 12; f++)
            codes[$"f{f}"] = 289 + f;
        (string, int)[] named =
        [
            ("space", 32), ("'", 39), (",", 44), ("-", 45), (".", 46), ("/", 47), (";", 59), ("=", 61),
            ("[", 91), ("\\", 92), ("]", 93), ("`", 96),
            ("escape", 256), ("return", 257), ("tab", 258), ("backspace", 259), ("insert", 260), ("delete", 261),
            ("right", 262), ("left", 263), ("down", 264), ("up", 265), ("page up", 266), ("page down", 267),
            ("home", 268), ("end", 269), ("caps lock", 280), ("pause", 284),
            ("[.]", 330), ("[/]", 331), ("[*]", 332), ("[-]", 333), ("[+]", 334), ("enter", 335),
            ("left shift", 340), ("left ctrl", 341), ("left alt", 342), ("left cmd", 343),
            ("right shift", 344), ("right ctrl", 345), ("right alt", 346), ("right cmd", 347),
        ];
        foreach ((string name, int code) in named)
            codes[name] = code;
        for (int m = 0; m < MouseButtons; m++)
            codes[$"mouse {m}"] = FirstMouseButton + m;
        for (int b = 0; b < GamepadButtons; b++)
            codes[$"joystick button {b}"] = FirstGamepadButton + b;
        return codes;
    }

    /// <summary>The code of a name (case and surrounding spaces ignored); <see cref="None"/> for an empty name.</summary>
    /// <returns>False when the name is not a known key or button.</returns>
    public static bool TryParse(string? name, out int code)
    {
        string key = (name ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0)
        {
            code = None;
            return true;
        }
        return Codes.TryGetValue(key, out code);
    }
}
