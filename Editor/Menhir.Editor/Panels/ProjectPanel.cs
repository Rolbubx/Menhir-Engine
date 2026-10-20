using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Projects;

namespace Menhir.Editor.Panels;

/// <summary>
/// Browser for the project's Assets/ folder: folder tree on the left, content of the
/// current folder on the right. Supports creating folders, renaming (F2), deleting (Del)
/// and moving by drag and drop. Every change goes through <see cref="AssetDatabase"/>
/// so .meta files follow their asset.
/// </summary>
public sealed class ProjectPanel : EditorPanel
{
    private const string AssetPayload = "MENHIR_ASSET";
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

    public ProjectPanel(EditorContext context, Action<AssetEntry> openScene)
    {
        _context = context;
        _openScene = openScene;
    }

    public override string Title => "Project";

    private AssetDatabase? Assets => _context.Project?.Assets;

    protected override void DrawContent()
    {
        if (_context.Project is not { } project)
        {
            Placeholder("No project open. Start the editor with --project <folder> " +
                        "(File > Open Project arrives in Phase 6).");
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
                DrawFolderNode(AssetPaths.Root);
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
        if (ImGui.Button("+"))
            ImGui.OpenPopup("CreateAsset");
        if (ImGui.BeginPopup("CreateAsset"))
        {
            DrawCreateMenuItems();
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
            _context.RefreshAssets();
        ImGui.SameLine();
        DrawBreadcrumbs();

        if (_error != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
            if (ImGui.IsItemClicked())
                _error = null;
        }
    }

    /// <summary>"Assets > Textures > Rock": each part opens that folder and accepts drops.</summary>
    private void DrawBreadcrumbs()
    {
        string[] parts = _currentFolder.Split('/');
        string path = "";
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        for (int i = 0; i < parts.Length; i++)
        {
            path = i == 0 ? parts[0] : AssetPaths.Combine(path, parts[i]);
            if (i > 0)
            {
                ImGui.SameLine(0, 0);
                ImGui.TextDisabled(">");
                ImGui.SameLine(0, 0);
            }
            if (ImGui.Button($"{parts[i]}##crumb{i}"))
                Navigate(path);
            DrawDropTarget(path);
        }
        ImGui.PopStyleColor();
    }

    private void DrawFolderNode(string folderPath)
    {
        IReadOnlyList<AssetEntry> children = Assets!.GetChildren(folderPath);
        bool hasSubfolders = children.Any(c => c.IsFolder);

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick |
                                   ImGuiTreeNodeFlags.SpanAvailWidth;
        if (folderPath == AssetPaths.Root)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasSubfolders)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (folderPath == _currentFolder)
            flags |= ImGuiTreeNodeFlags.Selected;

        // Open the parents of a folder reached from the content list, so it is visible in the tree.
        if (_revealCurrentFolder && folderPath != _currentFolder && AssetPaths.IsSameOrInside(_currentFolder, folderPath))
            ImGui.SetNextItemOpen(true);

        bool open = ImGui.TreeNodeEx($"{AssetPaths.GetName(folderPath)}##{folderPath}", flags);
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
        IReadOnlyList<AssetEntry> entries = Assets!.GetChildren(_currentFolder);

        ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##entries", 2, flags))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 6);
            foreach (AssetEntry entry in entries)
                DrawEntryRow(entry);
            ImGui.EndTable();
        }

        if (entries.Count == 0)
            Placeholder("This folder is empty.");

        DrawEmptyArea();
    }

    private void DrawEntryRow(AssetEntry entry)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.PushID(entry.Path);

        DrawTypeSwatch(entry);
        ImGui.SameLine();

        if (entry.Path == _renamingPath)
        {
            DrawRenameField(entry);
        }
        else
        {
            bool selected = _context.SelectedAssetGuid == entry.Guid;
            if (ImGui.Selectable(entry.Name, selected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
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

        ImGui.TableSetColumnIndex(1);
        ImGui.TextDisabled(entry.Meta.Importer);

        ImGui.PopID();
    }

    /// <summary>Small colored square standing in for an icon until there are real ones.</summary>
    private static void DrawTypeSwatch(AssetEntry entry)
    {
        Vector4 color = entry.Meta.Importer switch
        {
            AssetImporters.Folder => new(0.86f, 0.70f, 0.32f, 1f),
            AssetImporters.Scene => new(0.40f, 0.62f, 0.95f, 1f),
            AssetImporters.Texture => new(0.72f, 0.50f, 0.90f, 1f),
            AssetImporters.Model => new(0.45f, 0.80f, 0.50f, 1f),
            AssetImporters.Script => new(0.40f, 0.82f, 0.85f, 1f),
            _ => new(0.55f, 0.55f, 0.55f, 1f),
        };
        float size = ImGui.GetTextLineHeight() * 0.6f;
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 min = cursor + new Vector2(0, (ImGui.GetTextLineHeight() - size) * 0.5f);
        ImGui.GetWindowDrawList().AddRectFilled(min, min + new Vector2(size), ImGui.GetColorU32(color), 2f);
        ImGui.Dummy(new Vector2(size, ImGui.GetTextLineHeight()));
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
        if (ImGui.MenuItem("Folder"))
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
        if (_renamingPath != null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
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
        byte unused = 0;
        ImGui.SetDragDropPayload(AssetPayload, (IntPtr)(&unused), 1);
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

        ImGui.SetNextItemWidth(-float.Epsilon);
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
            if (name.Trim() != entry.Name)
                _pendingChange = () => Assets!.Rename(path, name);
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
            Console.Error.WriteLine($"Project: {e.Message}");
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
        }
    }
}
