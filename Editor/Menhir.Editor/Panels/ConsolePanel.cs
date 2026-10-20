using System.Numerics;
using System.Text;
using ImGuiNET;
using Menhir.Editor.Logging;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Editor log, like Unity's Console: one line per message, filters per level,
/// a text search, Collapse for repeated messages, and the full text of the
/// selected message (with its stack trace, if any) below the list.
/// </summary>
public sealed class ConsolePanel : EditorPanel
{
    private static readonly Vector4 InfoColor = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.75f, 0.25f, 1f);
    private static readonly Vector4 ErrorColor = new(0.95f, 0.35f, 0.3f, 1f);

    private const float MinPaneHeight = 40f;
    private const float SplitterThickness = 6f;

    private readonly EditorLog _log;
    private readonly LogFilter _filter;

    // Rows are rebuilt only when the log or the filter changed.
    private List<LogRow> _rows = [];
    private LogCounts _counts;
    private int _builtVersion = -1;
    private (bool, bool, bool, bool, string) _builtFilter;

    private LogEntry? _selected;
    private bool _stickToBottom = true;
    private float _lastListHeight;
    private float _detailsHeight = 110f;

    public ConsolePanel(EditorLog log, LogFilter filter)
    {
        _log = log;
        _filter = filter;
    }

    public override string Title => "Console";

    protected override void DrawContent()
    {
        RebuildRowsIfNeeded();
        DrawToolbar();
        ImGui.Separator();

        float available = ImGui.GetContentRegionAvail().Y;
        _detailsHeight = Math.Clamp(_detailsHeight, MinPaneHeight, Math.Max(MinPaneHeight, available - MinPaneHeight - SplitterThickness));
        float listHeight = Math.Max(MinPaneHeight, available - _detailsHeight - SplitterThickness);

        DrawList(listHeight);
        DrawSplitter();
        DrawDetails();
    }

    private void RebuildRowsIfNeeded()
    {
        var filterKey = (_filter.ShowInfo, _filter.ShowWarnings, _filter.ShowErrors, _filter.Collapse, _filter.Search);
        if (_builtVersion == _log.Version && _builtFilter == filterKey)
            return;

        // Read the version first: a message added meanwhile triggers another rebuild next frame.
        _builtVersion = _log.Version;
        _builtFilter = filterKey;
        List<LogEntry> entries = _log.GetEntries();
        _rows = LogView.Build(entries, _filter);
        _counts = LogView.Count(entries);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            _log.Clear();
            _selected = null;
        }
        ImGui.SameLine();
        bool collapse = _filter.Collapse;
        if (ToggleButton("Collapse", ref collapse))
            _filter.Collapse = collapse;
        ImGui.SameLine();

        // Level toggles are right-aligned; the search field takes the space in between.
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float togglesWidth = LevelToggleWidth(_counts.Info) + LevelToggleWidth(_counts.Warnings)
                             + LevelToggleWidth(_counts.Errors) + 2 * spacing;

        string search = _filter.Search;
        ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - togglesWidth - spacing));
        if (ImGui.InputTextWithHint("##search", "Search", ref search, 256))
            _filter.Search = search;

        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - togglesWidth));
        bool show = _filter.ShowInfo;
        if (LevelToggle("##info", _counts.Info, InfoColor, ref show, "Show messages"))
            _filter.ShowInfo = show;
        ImGui.SameLine();
        show = _filter.ShowWarnings;
        if (LevelToggle("##warnings", _counts.Warnings, WarningColor, ref show, "Show warnings"))
            _filter.ShowWarnings = show;
        ImGui.SameLine();
        show = _filter.ShowErrors;
        if (LevelToggle("##errors", _counts.Errors, ErrorColor, ref show, "Show errors"))
            _filter.ShowErrors = show;
    }

    private static bool ToggleButton(string label, ref bool value, Vector2 size = default)
    {
        if (value)
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        bool clicked = ImGui.Button(label, size);
        if (value)
            ImGui.PopStyleColor();
        if (clicked)
            value = !value;
        return clicked;
    }

    private static float IconSize => ImGui.GetTextLineHeight() * 0.55f;

    private static float LevelToggleWidth(int count) =>
        IconSize + 4f + ImGui.CalcTextSize(count.ToString()).X + ImGui.GetStyle().FramePadding.X * 2;

    /// <summary>A toggle button showing a level's icon and message count.</summary>
    private static bool LevelToggle(string id, int count, Vector4 color, ref bool value, string tooltip)
    {
        Vector2 start = ImGui.GetCursorScreenPos();
        float height = ImGui.GetFrameHeight();
        bool clicked = ToggleButton(id, ref value, new Vector2(LevelToggleWidth(count), height));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float x = start.X + ImGui.GetStyle().FramePadding.X;
        DrawLevelIcon(drawList, new Vector2(x + IconSize / 2, start.Y + height / 2), value ? color : color with { W = 0.35f });
        uint textColor = ImGui.GetColorU32(value ? ImGuiCol.Text : ImGuiCol.TextDisabled);
        drawList.AddText(new Vector2(x + IconSize + 4f, start.Y + ImGui.GetStyle().FramePadding.Y), textColor, count.ToString());
        return clicked;
    }

    private static void DrawLevelIcon(ImDrawListPtr drawList, Vector2 center, Vector4 color) =>
        drawList.AddCircleFilled(center, IconSize / 2, ImGui.GetColorU32(color));

    private static Vector4 ColorOf(LogLevel level) => level switch
    {
        LogLevel.Warning => WarningColor,
        LogLevel.Error => ErrorColor,
        _ => InfoColor,
    };

    private unsafe void DrawList(float height)
    {
        if (!ImGui.BeginChild("##messages", new Vector2(0, height)))
        {
            ImGui.EndChild();
            return;
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        float timeWidth = ImGui.CalcTextSize("00:00:00 ").X;
        uint stripeColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.025f));
        uint timeColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);

        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        clipper.Begin(_rows.Count, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                LogRow row = _rows[i];
                LogEntry entry = row.Entry;
                Vector2 start = ImGui.GetCursorScreenPos();
                float width = ImGui.GetContentRegionAvail().X;
                if (i % 2 == 1)
                    drawList.AddRectFilled(start, start + new Vector2(width, rowHeight), stripeColor);

                ImGui.PushID((int)entry.Sequence);
                if (ImGui.Selectable("##row", IsSelected(entry), ImGuiSelectableFlags.None, new Vector2(0, rowHeight - ImGui.GetStyle().ItemSpacing.Y)))
                    _selected = entry;
                if (ImGui.BeginPopupContextItem("##row-menu"))
                {
                    _selected = entry;
                    if (ImGui.MenuItem("Copy"))
                        ImGui.SetClipboardText(FullText(entry));
                    ImGui.EndPopup();
                }
                ImGui.PopID();

                float textY = start.Y + (rowHeight - ImGui.GetStyle().ItemSpacing.Y - ImGui.GetTextLineHeight()) / 2;
                float x = start.X + 4f;
                DrawLevelIcon(drawList, new Vector2(x + IconSize / 2, textY + ImGui.GetTextLineHeight() / 2), ColorOf(entry.Level));
                x += IconSize + 6f;
                drawList.AddText(new Vector2(x, textY), timeColor, entry.Time.ToString("HH:mm:ss"));
                x += timeWidth;
                drawList.AddText(new Vector2(x, textY), textColor, entry.Summary);

                if (row.Count > 1)
                    DrawCountBadge(drawList, row.Count, start.X + width, textY);
            }
        }
        clipper.End();
        clipper.Destroy();

        if (_rows.Count == 0)
            ImGui.TextDisabled(_log.Count == 0 ? "No messages." : "No messages match the filter.");

        // Follow new messages, unless the user scrolled up to read older ones.
        // (While the list is resized, the scroll position lags a frame behind, so it is not checked.)
        if (_stickToBottom)
            ImGui.SetScrollHereY(1f);
        if (height == _lastListHeight)
            _stickToBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;
        _lastListHeight = height;

        if (_selected != null && ImGui.IsWindowFocused() && !ImGui.GetIO().WantTextInput && EditorInput.Shortcut(ImGuiKey.C))
            ImGui.SetClipboardText(FullText(_selected));

        ImGui.EndChild();
    }

    /// <summary>With Collapse, a row stands for every occurrence of its message.</summary>
    private bool IsSelected(LogEntry entry) =>
        _selected != null && (_selected == entry || (_filter.Collapse && SameMessage(_selected, entry)));

    private static bool SameMessage(LogEntry a, LogEntry b) =>
        a.Level == b.Level && a.Message == b.Message && a.Details == b.Details;

    private static void DrawCountBadge(ImDrawListPtr drawList, int count, float right, float textY)
    {
        string text = count > 999 ? "999+" : count.ToString();
        Vector2 size = ImGui.CalcTextSize(text);
        float padding = 5f;
        Vector2 max = new(right - 4f, textY + size.Y);
        Vector2 min = new(max.X - size.X - padding * 2, textY);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBgActive), size.Y / 2);
        drawList.AddText(new Vector2(min.X + padding, textY), ImGui.GetColorU32(ImGuiCol.Text), text);
    }

    /// <summary>Drag handle between the list and the details pane.</summary>
    private void DrawSplitter()
    {
        ImGui.InvisibleButton("##splitter", new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), SplitterThickness));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
        if (ImGui.IsItemActive())
            _detailsHeight -= ImGui.GetIO().MouseDelta.Y;

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float y = (min.Y + max.Y) / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.GetColorU32(ImGuiCol.Separator));
    }

    private void DrawDetails()
    {
        if (_selected is not { } entry)
        {
            ImGui.TextDisabled("Select a message to see all of it.");
            return;
        }

        ImGui.TextColored(ColorOf(entry.Level), entry.Level.ToString());
        ImGui.SameLine();
        ImGui.TextDisabled(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy"))
            ImGui.SetClipboardText(FullText(entry));

        // A read-only text field, so the text can be selected and copied in parts.
        string text = FullText(entry);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ImGui.GetStyle().Colors[(int)ImGuiCol.ChildBg]);
        ImGui.InputTextMultiline("##details", ref text, (uint)Encoding.UTF8.GetByteCount(text) + 1,
            ImGui.GetContentRegionAvail(), ImGuiInputTextFlags.ReadOnly);
        ImGui.PopStyleColor();
    }

    private static string FullText(LogEntry entry) =>
        entry.Details == null ? entry.Message : entry.Message + "\n\n" + entry.Details;
}
