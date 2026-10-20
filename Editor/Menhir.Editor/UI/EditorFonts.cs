using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Logging;
using Silk.NET.OpenGL.Extensions.ImGui;

namespace Menhir.Editor.UI;

/// <summary>Editor font: JetBrains Mono (SIL Open Font License, see Resources/Fonts).</summary>
public static class EditorFonts
{
    private const int FontSize = 15;

    public static string FontPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", "JetBrainsMono-Regular.ttf");

    // Basic Latin + Latin-1 (accents), General Punctuation (dashes, ellipsis), arrows.
    // ImGui reads this array when building the atlas, so it lives in unmanaged memory for the whole run.
    private static readonly Lazy<IntPtr> GlyphRanges = new(() =>
    {
        // ImWchar is 16-bit; every value here fits in a short.
        short[] ranges = [0x0020, 0x00FF, 0x2000, 0x206F, 0x2190, 0x21FF, 0];
        IntPtr ptr = Marshal.AllocHGlobal(ranges.Length * sizeof(short));
        Marshal.Copy(ranges, 0, ptr, ranges.Length);
        return ptr;
    });

    /// <summary>Font config for the ImGui controller, or null (ImGui default font) if the file is missing.</summary>
    public static ImGuiFontConfig? CreateConfig()
    {
        if (!File.Exists(FontPath))
        {
            Log.Warning($"Editor font not found at {FontPath}, using the ImGui default font.");
            return null;
        }
        return new ImGuiFontConfig(FontPath, FontSize, _ => GlyphRanges.Value);
    }
}
