using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;
using Menhir.Editor.Scripting;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Browser for the project's Assets/ folder: folder tree on the left, content of the
/// current folder on the right. Supports creating folders, renaming (F2), deleting (Del)
/// and moving by drag and drop. Every change goes through <see cref="AssetDatabase"/>
/// so .meta files follow their asset.
/// </summary>
public sealed class ProjectPanel : EditorPanel
{
    /// <summary>Drag and drop payload of an asset: its GUID (16 bytes).</summary>
    public const string AssetPayload = "MENHIR_ASSET";
    private const string DeletePopup = "Delete Asset?";

    private readonly EditorContext _context;
    private readonly Action<AssetEntry> _openScene;

    private string _currentFolder = AssetPaths.Root;
    private bool _revealCurrentFolder;

    // File system changes are queued while the lists are drawn and applied afterwards.
    private Action? _pendingChange;

    // ImGui payloads are raw bytes; the dragged path is kept here instead.
    private string? _draggedPath;

    private string? _renamingPath;
    private string _renameBuffer = "";
    private bool _focusRenameField;

    private string? _deletePath;

    private string? _error;
    private string _search = "";

    /// <summary>Icons (true) or a list with a Type column (false). Saved with the editor preferences.</summary>
    public bool GridView { get; set; } = true;

    public ProjectPanel(EditorContext context, Action<AssetEntry> openScene)
    {
        _context = context;
        _openScene = openScene;
    }

    public override string Title => "Project";

    public override string Icon => Icons.Project;

    private AssetDatabase? Assets => _context.Project?.Assets;

    protected override void DrawContent()
    {
        if (_context.Project is not { } project)
        {
            Placeholder("No project open. Use File > Open Project or File > New Project.");
            return;
        }

        // The current folder may have been renamed, moved or deleted (here or outside the editor).
        if (!project.Assets.IsFolder(_currentFolder))
            _currentFolder = AssetPaths.Root;

        DrawToolbar();
        ImGui.Separator();

        if (ImGui.BeginTable("##projectSplit", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Folders", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch, 0.7f);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (ImGui.BeginChild("##folders"))
            {
                EditorTheme.PushSelectionColors();
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { Y = 0 });
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding with { Y = 2 });
                DrawFolderNode(AssetPaths.Root);
                ImGui.PopStyleVar(2);
                EditorTheme.PopSelectionColors();
            }
            ImGui.EndChild();

            ImGui.TableSetColumnIndex(1);
            if (ImGui.BeginChild("##content"))
                DrawFolderContent();
            ImGui.EndChild();

            ImGui.EndTable();
        }

        HandleShortcuts();
        DrawDeleteConfirmation();

        Action? change = _pendingChange;
        _pendingChange = null;
        if (change != null)
            Apply(change);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button($"{Icons.Plus}##create"))
            ImGui.OpenPopup("CreateAsset");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Create");
        if (ImGui.BeginPopup("CreateAsset"))
        {
            DrawCreateMenuItems();
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        if (EditorTheme.FlatButton($"{Icons.Refresh}##refresh"))
            _context.RefreshAssets();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Refresh (look for files changed on disk)");

        if (_error != null)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(EditorTheme.Error, $"{Icons.Error}  {_error}");
            if (ImGui.IsItemClicked())
                _error = null;
        }

        // Search and view mode on the right.
        float toggleWidth = ImGui.GetFrameHeight();
        float searchWidth = MathF.Min(ImGui.GetFontSize() * 14, ImGui.GetContentRegionAvail().X * 0.5f);
        ImGui.SameLine(ImGui.GetContentRegionMax().X - searchWidth - toggleWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##search", $"{Icons.Search}  Search", ref _search, 128);
        ImGui.SameLine();
        if (EditorTheme.FlatButton($"{(GridView ? Icons.ListView : Icons.GridView)}##view", new Vector2(toggleWidth, 0)))
            GridView = !GridView;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(GridView ? "Show as a list" : "Show as icons");
    }

    /// <summary>"Assets › Textures › Rock": each part opens that folder and accepts drops.</summary>
    private void DrawBreadcrumbs()
    {
        string[] parts = _currentFolder.Split('/');
        string path = "";
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding with { X = 3 });
        for (int i = 0; i < parts.Length; i++)
        {
            path = i == 0 ? parts[0] : AssetPaths.Combine(path, parts[i]);
            if (i > 0)
            {
                ImGui.SameLine(0, 0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled(Icons.ChevronRight);
                ImGui.SameLine(0, 0);
            }
            ImGui.PushFont(i == parts.Length - 1 ? EditorFonts.Bold : ImGui.GetFont());
            if (EditorTheme.FlatButton($"{parts[i]}##crumb{i}"))
                Navigate(path);
            ImGui.PopFont();
            DrawDropTarget(path);
        }
        ImGui.PopStyleVar();
    }

    private void DrawFolderNode(string folderPath)
    {
        IReadOnlyList<AssetEntry> children = Assets!.GetChildren(folderPath);
        bool hasSubfolders = children.Any(c => c.IsFolder);

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick |
                                   ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding;
        if (folderPath == AssetPaths.Root)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasSubfolders)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (folderPath == _currentFolder)
            flags |= ImGuiTreeNodeFlags.Selected;

        // Open the parents of a folder reached from the content list, so it is visible in the tree.
        if (_revealCurrentFolder && folderPath != _currentFolder && AssetPaths.IsSameOrInside(_currentFolder, folderPath))
            ImGui.SetNextItemOpen(true);

        // The icon is drawn over the label's leading spaces, so it can have its own color.
        Vector2 labelPos = ImGui.GetCursorScreenPos() + new Vector2(ImGui.GetTreeNodeToLabelSpacing(), ImGui.GetStyle().FramePadding.Y);
        bool open = ImGui.TreeNodeEx($"     {AssetPaths.GetName(folderPath)}##{folderPath}", flags);
        ImGui.GetWindowDrawList().AddText(labelPos, ImGui.GetColorU32(AssetIcons.Color(AssetImporters.Folder)),
            open && hasSubfolders ? Icons.FolderOpen : Icons.Folder);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
            _currentFolder = folderPath;
        if (folderPath != AssetPaths.Root)
        {
            DrawDragSource(Assets.FindByPath(folderPath)!);
            if (ImGui.BeginPopupContextItem())
            {
                DrawEntryMenuItems(Assets.FindByPath(folderPath)!);
                ImGui.EndPopup();
            }
        }
        DrawDropTarget(folderPath);

        if (open)
        {
            foreach (AssetEntry child in children)
            {
                if (child.IsFolder)
                    DrawFolderNode(child.Path);
            }
            ImGui.TreePop();
        }

        if (folderPath == AssetPaths.Root)
            _revealCurrentFolder = false;
    }

    private void DrawFolderContent()
    {
        string search = _search.Trim();
        IReadOnlyList<AssetEntry> entries;
        if (search.Length > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"Search results for '{search}' in Assets");
            entries = Assets!.GetAllEntries()
                .Where(e => e.Path != AssetPaths.Root && e.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => !e.IsFolder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            DrawBreadcrumbs();
            entries = Assets!.GetChildren(_currentFolder);
        }
        ImGui.Separator();

        EditorTheme.PushSelectionColors();
        if (GridView)
            DrawGrid(entries);
        else
            DrawList(entries, showPath: search.Length > 0);
        EditorTheme.PopSelectionColors();

        if (entries.Count == 0)
            Placeholder(search.Length > 0 ? "Nothing matches the search." : "This folder is empty.");

        DrawEmptyArea();
    }

    private void DrawList(IReadOnlyList<AssetEntry> entries, bool showPath)
    {
        ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##entries", 2, flags))
            return;
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 6);
        foreach (AssetEntry entry in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.PushID(entry.Path);

            ImGui.TextColored(AssetIcons.Color(entry), AssetIcons.Icon(entry));
            ImGui.SameLine();
            if (entry.Path == _renamingPath)
            {
                ImGui.SetNextItemWidth(-float.Epsilon);
                DrawRenameField(entry);
            }
            else
            {
                bool selected = _context.SelectedAssetGuid == entry.Guid;
                Vector2 textPos = ImGui.GetCursorScreenPos();
                bool clicked = ImGui.Selectable(entry.Name, selected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                HandleEntryItem(entry, clicked);
                // Search results come from any folder: show which one, dimmed, after the name.
                if (showPath)
                {
                    textPos.X += ImGui.CalcTextSize(entry.Name + "   ").X;
                    ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(ImGuiCol.TextDisabled),
                        AssetPaths.GetParent(entry.Path) ?? "");
                }
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled(entry.Meta.Importer);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    /// <summary>Unity's default view: a big icon per asset with its name underneath.</summary>
    private void DrawGrid(IReadOnlyList<AssetEntry> entries)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float cellWidth = EditorFonts.LargeIconSize + 36f;
        float cellHeight = EditorFonts.LargeIconSize + ImGui.GetTextLineHeight() + 14f;
        float spacing = style.ItemSpacing.X;
        int columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (cellWidth + spacing)));
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        ImGui.Spacing();
        for (int i = 0; i < entries.Count; i++)
        {
            AssetEntry entry = entries[i];
            if (i % columns != 0)
                ImGui.SameLine(0, spacing);
            ImGui.PushID(entry.Path);

            Vector2 min = ImGui.GetCursorScreenPos();
            bool selected = _context.SelectedAssetGuid == entry.Guid;
            bool clicked = ImGui.Selectable("##cell", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(cellWidth, cellHeight));
            if (entry.Path != _renamingPath)
                HandleEntryItem(entry, clicked);
            if (ImGui.IsItemHovered() && entry.Path != _renamingPath)
                ImGui.SetTooltip(entry.Path);

            // Icon centered at the top, name centered below (shortened with "…" when too long).
            ImGui.PushFont(EditorFonts.LargeIcons);
            string icon = AssetIcons.Icon(entry);
            Vector2 iconSize = ImGui.CalcTextSize(icon);
            drawList.AddText(min + new Vector2((cellWidth - iconSize.X) / 2, 4), ImGui.GetColorU32(AssetIcons.Color(entry)), icon);
            ImGui.PopFont();

            float nameY = min.Y + EditorFonts.LargeIconSize + 8f;
            if (entry.Path == _renamingPath)
            {
                Vector2 after = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(new Vector2(min.X, nameY - 2));
                ImGui.SetNextItemWidth(cellWidth);
                DrawRenameField(entry);
                ImGui.SetCursorScreenPos(after);
            }
            else
            {
                string name = Ellipsize(entry.Name, cellWidth - 4f);
                float nameWidth = ImGui.CalcTextSize(name).X;
                drawList.AddText(new Vector2(min.X + (cellWidth - nameWidth) / 2, nameY), ImGui.GetColorU32(ImGuiCol.Text), name);
            }
            ImGui.PopID();
        }
    }

    private static string Ellipsize(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;
        for (int length = text.Length - 1; length > 0; length--)
        {
            string shortened = text[..length] + "…";
            if (ImGui.CalcTextSize(shortened).X <= maxWidth)
                return shortened;
        }
        return "…";
    }

    /// <summary>Click, double-click, drag and drop, and context menu of an asset in the list or the grid.</summary>
    private void HandleEntryItem(AssetEntry entry, bool clicked)
    {
        if (clicked)
        {
            _context.SelectedAssetGuid = entry.Guid;
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                Activate(entry);
        }
        DrawDragSource(entry);
        if (entry.IsFolder)
            DrawDropTarget(entry.Path);
        if (ImGui.BeginPopupContextItem())
        {
            _context.SelectedAssetGuid = entry.Guid;
            DrawEntryMenuItems(entry);
            ImGui.EndPopup();
        }
    }

    private void DrawEntryMenuItems(AssetEntry entry)
    {
        if ((entry.IsFolder || IsScene(entry)) && ImGui.MenuItem("Open"))
            Activate(entry);
        if (ImGui.MenuItem("Rename", "F2"))
            BeginRename(entry);
        if (ImGui.MenuItem("Delete", "Del"))
            _deletePath = entry.Path;
        ImGui.Separator();
        if (ImGui.MenuItem(RevealLabel))
            Reveal(entry.Path);
    }

    private void DrawCreateMenuItems()
    {
        if (ImGui.MenuItem($"{Icons.Folder}  Folder"))
        {
            string parent = _currentFolder;
            _pendingChange = () =>
            {
                string path = Assets!.CreateFolder(parent);
                if (Assets.FindByPath(path) is { } created)
                {
                    _context.SelectedAssetGuid = created.Guid;
                    BeginRename(created);
                }
            };
        }
        if (ImGui.MenuItem($"{Icons.Script}  C++ Script"))
        {
            string parent = _currentFolder;
            _pendingChange = () =>
            {
                string path = ScriptTemplate.Create(Assets!, parent, cpp: true);
                if (Assets!.FindByPath(path) is { } created)
                {
                    _context.SelectedAssetGuid = created.Guid;
                    BeginRename(created);
                }
            };
        }
        if (ImGui.MenuItem($"{Icons.Script}  C Script"))
        {
            string parent = _currentFolder;
            _pendingChange = () =>
            {
                string path = ScriptTemplate.Create(Assets!, parent);
                if (Assets!.FindByPath(path) is { } created)
                {
                    _context.SelectedAssetGuid = created.Guid;
                    BeginRename(created);
                }
            };
        }
    }

    /// <summary>Space below the list: click to deselect, right-click for the folder menu.</summary>
    private void DrawEmptyArea()
    {
        Vector2 size = ImGui.GetContentRegionAvail();
        size.Y = MathF.Max(size.Y, ImGui.GetFrameHeight());
        if (size.X < 1)
            return;

        ImGui.InvisibleButton("##contentEmptyArea", size);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _context.SelectedAssetGuid = null;
        if (ImGui.BeginPopupContextItem("ContentContext"))
        {
            if (ImGui.BeginMenu("Create"))
            {
                DrawCreateMenuItems();
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(RevealLabel))
                Reveal(_currentFolder);
            if (ImGui.MenuItem("Refresh"))
                _context.RefreshAssets();
            ImGui.EndPopup();
        }
    }

    private void HandleShortcuts()
    {
        // A change already asked for this frame (the Enter that ends a rename) is not also a shortcut.
        if (_renamingPath != null || _pendingChange != null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;
        if (_context.SelectedAsset is not { } selected)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            _deletePath = selected.Path;
        else if (ImGui.IsKeyPressed(ImGuiKey.F2))
            BeginRename(selected);
        else if (ImGui.IsKeyPressed(ImGuiKey.Enter))
            Activate(selected);
    }

    private unsafe void DrawDragSource(AssetEntry entry)
    {
        if (!ImGui.BeginDragDropSource())
            return;
        _draggedPath = entry.Path;
        Guid guid = entry.Guid;
        ImGui.SetDragDropPayload(AssetPayload, (IntPtr)(&guid), (uint)sizeof(Guid));
        ImGui.TextUnformatted(entry.Name);
        ImGui.EndDragDropSource();
    }

    private unsafe void DrawDropTarget(string folderPath)
    {
        if (!ImGui.BeginDragDropTarget())
            return;
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(AssetPayload);
        if (payload.NativePtr != null && _draggedPath is { } dragged &&
            AssetPaths.GetParent(dragged) != folderPath && !AssetPaths.IsSameOrInside(folderPath, dragged))
        {
            _pendingChange = () => Assets!.Move(dragged, folderPath);
        }
        ImGui.EndDragDropTarget();
    }

    private void BeginRename(AssetEntry entry)
    {
        // Folders are renamed in the content list, so show the one that contains it.
        _currentFolder = AssetPaths.GetParent(entry.Path) ?? AssetPaths.Root;
        _renamingPath = entry.Path;
        _renameBuffer = entry.Name;
        _focusRenameField = true;
    }

    private void DrawRenameField(AssetEntry entry)
    {
        if (_focusRenameField)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRenameField = false;
        }

        bool submitted = ImGui.InputText("##rename", ref _renameBuffer, 256,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _renamingPath = null;
        }
        else if (submitted || ImGui.IsItemDeactivated())
        {
            string path = entry.Path;
            string name = _renameBuffer;
            _renamingPath = null;
            string oldName = entry.Name;
            if (name.Trim() != oldName)
                _pendingChange = () =>
                {
                    string renamed = Assets!.Rename(path, name);
                    // A new script keeps its class in step with its file name.
                    ScriptTemplate.RenameClass(Assets.ToAbsolutePath(renamed), oldName, AssetPaths.GetName(renamed));
                };
        }
    }

    private void DrawDeleteConfirmation()
    {
        if (_deletePath != null && !ImGui.IsPopupOpen(DeletePopup))
            ImGui.OpenPopup(DeletePopup);

        bool open = true;
        if (!ImGui.BeginPopupModal(DeletePopup, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            _deletePath = null;
            return;
        }

        string path = _deletePath ?? "";
        bool isFolder = Assets!.IsFolder(path);
        ImGui.TextUnformatted(path);
        ImGui.TextDisabled(isFolder
            ? "The folder and everything in it will be deleted from disk. This cannot be undone."
            : "The file will be deleted from disk. This cannot be undone.");
        ImGui.Spacing();

        if (ImGui.Button("Delete") || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            _pendingChange = () => Assets.Delete(path);
            _deletePath = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _deletePath = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>Double-click: opens a folder or a scene.</summary>
    private void Activate(AssetEntry entry)
    {
        if (entry.IsFolder)
            _pendingChange = () => Navigate(entry.Path);
        else if (IsScene(entry))
            _pendingChange = () => _openScene(entry);
        else if (entry.Meta.Importer == AssetImporters.Script)
            _pendingChange = () => OpenInEditor(entry);
    }

    /// <summary>Opens a script with the program the OS uses for .cs files.</summary>
    private void OpenInEditor(AssetEntry entry)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Assets!.ToAbsolutePath(entry.Path)) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error($"Could not open {entry.Path}: {e.Message}");
        }
    }

    private static bool IsScene(AssetEntry entry) => entry.Meta.Importer == AssetImporters.Scene;

    private void Navigate(string folderPath)
    {
        _currentFolder = folderPath;
        _revealCurrentFolder = true;
    }

    /// <summary>Runs a file operation; failures (name taken, file locked...) are shown in the toolbar.</summary>
    private void Apply(Action change)
    {
        try
        {
            change();
            _error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or InvalidOperationException)
        {
            _error = e.Message;
            Log.Error(e.Message);
            _context.RefreshAssets();
        }
    }

    private static string RevealLabel =>
        OperatingSystem.IsWindows() ? "Show in Explorer" :
        OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Open Containing Folder";

    /// <summary>Opens the OS file browser on an asset (selected on Windows) or on a folder.</summary>
    private void Reveal(string assetPath)
    {
        string absolute = Assets!.ToAbsolutePath(assetPath);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{absolute}\"");
            }
            else
            {
                string folder = Assets.IsFolder(assetPath) ? absolute : Path.GetDirectoryName(absolute)!;
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _error = $"Could not open the file browser: {e.Message}";
            Log.Error(_error);
        }
    }
}
