using ImGuiNET;
using Silk.NET.Input;

namespace Menhir.Editor.UI;

/// <summary>
/// Silk's ImGui controller (2.23) only writes the legacy io.KeyCtrl/KeyShift/... fields,
/// which ImGui 1.90 overwrites from the ImGuiMod_* key events. Without those events Ctrl
/// never reaches ImGui, so text field shortcuts (Ctrl+A/C/V/Z) do nothing. This forwards
/// the modifier keys as ImGuiMod_* events.
/// </summary>
public static class ImGuiModifierKeys
{
    public static void Attach(IInputContext input)
    {
        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (k, key, _) => Forward(k);
            keyboard.KeyUp += (k, key, _) => Forward(k);
        }
    }

    private static void Forward(IKeyboard keyboard)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight));
    }
}
