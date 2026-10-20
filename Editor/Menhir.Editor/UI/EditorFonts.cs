using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Logging;

namespace Menhir.Editor.UI;

/// <summary>
/// Editor fonts: Inter for the UI (SIL Open Font License), JetBrains Mono for logs, paths
/// and GUIDs (SIL OFL), each with the Lucide icons (ISC) merged in. See Resources/Fonts.
/// </summary>
public static class EditorFonts
{
    public const float Size = 15f;
    private const float MonoSize = 14f;

    /// <summary>Size of the icons drawn as thumbnails in the Project panel's grid view.</summary>
    public const float LargeIconSize = 44f;

    private static readonly string FontDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");

    /// <summary>Semi-bold UI font, for headers. Default (null) when the files are missing.</summary>
    public static ImFontPtr Bold { get; private set; }

    public static ImFontPtr Mono { get; private set; }

    /// <summary>Only the icons in <see cref="LargeIcons"/>, at <see cref="LargeIconSize"/>.</summary>
    public static ImFontPtr LargeIcons { get; private set; }

    private static readonly string[] LargeIconSet =
        [Icons.Folder, Icons.File, Icons.Scene, Icons.Texture, Icons.Model, Icons.Script, Icons.Text];

    // ImGui reads glyph ranges when it builds the atlas (and keeps the pointers), so they live in
    // unmanaged memory for the whole run. ImWchar is 16-bit; every value here fits in a ushort.
    // Text: Basic Latin + Latin-1 (accents), General Punctuation (dashes, ellipsis), arrows.
    private static readonly IntPtr TextRanges = AllocRanges([0x0020, 0x00FF, 0x2000, 0x206F, 0x2190, 0x21FF]);
    // Lucide uses the private use area from U+E000.
    private static readonly IntPtr IconRanges = AllocRanges([0xE000, 0xE8FF]);
    private static readonly IntPtr LargeIconRanges = AllocRanges(LargeIconSet.SelectMany(i => new ushort[] { i[0], i[0] }).ToArray());

    private static IntPtr AllocRanges(ushort[] ranges)
    {
        IntPtr ptr = Marshal.AllocHGlobal((ranges.Length + 1) * sizeof(ushort));
        for (int i = 0; i < ranges.Length; i++)
            Marshal.WriteInt16(ptr, i * sizeof(ushort), (short)ranges[i]);
        Marshal.WriteInt16(ptr, ranges.Length * sizeof(ushort), 0);
        return ptr;
    }

    /// <summary>Adds the fonts to the atlas. Call while configuring ImGui, before the atlas is built.</summary>
    public static unsafe void Load()
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        string regular = Path.Combine(FontDirectory, "Inter-Regular.ttf");
        string semiBold = Path.Combine(FontDirectory, "Inter-SemiBold.ttf");
        string mono = Path.Combine(FontDirectory, "JetBrainsMono-Regular.ttf");
        string icons = Path.Combine(FontDirectory, "Lucide.ttf");

        string? missing = new[] { regular, semiBold, mono, icons }.FirstOrDefault(p => !File.Exists(p));
        if (missing != null)
        {
            Log.Warning($"Editor font not found at {missing}, using the ImGui default font.");
            atlas.AddFontDefault();
            Bold = Mono = LargeIcons = atlas.Fonts[0];
            return;
        }

        // The first font added is the default one.
        AddWithIcons(atlas, regular, Size, icons);
        Bold = AddWithIcons(atlas, semiBold, Size, icons);
        Mono = AddWithIcons(atlas, mono, MonoSize, icons);

        ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
        config.GlyphMinAdvanceX = LargeIconSize;
        LargeIcons = atlas.AddFontFromFileTTF(icons, LargeIconSize, config, LargeIconRanges);
        config.Destroy();
    }

    private static unsafe ImFontPtr AddWithIcons(ImFontAtlasPtr atlas, string path, float size, string iconsPath)
    {
        ImFontPtr font = atlas.AddFontFromFileTTF(path, size, null, TextRanges);

        // Icons are merged into the text font, slightly smaller, centered on the text and
        // all as wide, so labels with an icon line up.
        ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
        config.MergeMode = true;
        config.PixelSnapH = true;
        config.GlyphMinAdvanceX = size;
        config.GlyphOffset = new Vector2(0, 1.5f);
        atlas.AddFontFromFileTTF(iconsPath, size - 1f, config, IconRanges);
        config.Destroy();
        return font;
    }

    /// <summary>Text in the large icon font, e.g. a grid thumbnail.</summary>
    public static void LargeIcon(string icon, Vector4 color)
    {
        ImGui.PushFont(LargeIcons);
        ImGui.TextColored(color, icon);
        ImGui.PopFont();
    }
}
