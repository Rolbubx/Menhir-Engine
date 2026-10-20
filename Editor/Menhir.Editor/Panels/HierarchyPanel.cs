using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspector;
using Menhir.Editor.Scenes;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Entity tree of the active scene: select, create, delete (Delete key), rename
/// (double-click or F2), and reparent by dragging an entity onto another one,
/// or onto the empty area below the tree to make it a root.
/// </summary>
public sealed class HierarchyPanel : EditorPanel
{
    private const string EntityPayload = "MENHIR_ENTITY";

    private readonly EditorContext _context;

    // Scene changes are queued while the tree is drawn and applied afterwards,
    // so the entity list is never modified while it is being iterated.
    private Action? _pendingChange;

    private long _renamingId;
    private string _renameBuffer = "";
    private string _search = "";
    private bool _focusRenameField;

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public override string Title => "Hierarchy";

    public override string Icon => Icons.Hierarchy;

    private Scene Scene => _context.ActiveScene;

    protected override void DrawContent()
    {
        DrawToolbar();
        DrawSceneRow();

        EditorTheme.PushSelectionColors();
        // Rows touch each other, so the selection is one solid band like Unity's.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { Y = 0 });
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding with { Y = 2 });
        if (_search.Trim().Length > 0)
            DrawSearchResults(_search.Trim());
        else
        {
            foreach (Entity root in Scene.GetChildren(0))
                DrawEntityNode(root);
        }
        ImGui.PopStyleVar(2);
        EditorTheme.PopSelectionColors();

        DrawEmptyArea();
        HandleShortcuts();

        _pendingChange?.Invoke();
        _pendingChange = null;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button($"{Icons.Plus}##create"))
            ImGui.OpenPopup("CreateEntity");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Create an entity");
        if (ImGui.BeginPopup("CreateEntity"))
        {
            DrawCreateMenuItems(parentId: 0);
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-float.Epsilon);
        ImGui.InputTextWithHint("##search", $"{Icons.Search}  Search", ref _search, 128);
    }

    /// <summary>The scene's name above its entities, with a * when it has unsaved changes.</summary>
    private void DrawSceneRow()
    {
        ImGui.Spacing();
        Vector2 min = ImGui.GetCursorScreenPos() - new Vector2(ImGui.GetStyle().WindowPadding.X, 0);
        float height = ImGui.GetFrameHeight();
        Vector2 max = new(min.X + ImGui.GetWindowWidth(), min.Y + height);
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(EditorTheme.HeaderBar));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, min.Y + (height - ImGui.GetTextLineHeight()) / 2));
        ImGui.TextColored(AssetIcons.Color(AssetImporters.Scene), Icons.Scene);
        ImGui.SameLine();
        ImGui.PushFont(EditorFonts.Bold);
        ImGui.TextUnformatted(_context.IsSceneDirty ? Scene.Name + "*" : Scene.Name);
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_context.ActiveScenePath ?? "Not saved yet");
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, max.Y + 2));
    }

    /// <summary>While searching: every entity whose name matches, as a flat list.</summary>
    private void DrawSearchResults(string search)
    {
        bool any = false;
        foreach (Entity entity in Scene.Entities)
        {
            if (!entity.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            any = true;
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen |
                                       ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding;
            if (entity.LocalId == _context.SelectedEntityId)
                flags |= ImGuiTreeNodeFlags.Selected;
            ImGui.TreeNodeEx($"{EntityIcon(entity)}  {entity.Name}##found{entity.LocalId}", flags);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _context.SelectedEntityId = entity.LocalId;
            DrawContextMenu(entity);
        }
        if (!any)
            Placeholder("No entity matches.");
    }

    /// <summary>The icon of the entity's most telling component (light, sky, volume), or a box.</summary>
    private static string EntityIcon(Entity entity)
    {
        Component? main = entity.Components.FirstOrDefault(c => c is not MeshRenderer and not MissingComponent);
        return main != null ? ComponentDrawer.IconFor(main.GetType()) : Icons.Entity;
    }

    private void DrawEntityNode(Entity entity)
    {
        bool hasChildren = Scene.HasChildren(entity);
        bool renaming = entity.LocalId == _renamingId;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth |
                                   ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.FramePadding;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (entity.LocalId == _context.SelectedEntityId)
            flags |= ImGuiTreeNodeFlags.Selected;

        // The ID after ## stays stable when the entity is renamed, so the open state survives.
        string label = renaming ? EntityIcon(entity) : $"{EntityIcon(entity)}  {entity.Name}";
        bool open = ImGui.TreeNodeEx($"{label}##entity{entity.LocalId}", flags);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
            _context.SelectedEntityId = entity.LocalId;
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            BeginRename(entity);

        DrawDragSource(entity);
        DrawDropTarget(entity.LocalId);
        DrawContextMenu(entity);

        if (renaming)
        {
            ImGui.SameLine();
            DrawRenameField(entity);
        }

        if (open)
        {
            foreach (Entity child in Scene.GetChildren(entity.LocalId))
                DrawEntityNode(child);
            ImGui.TreePop();
        }
    }

    private static unsafe void DrawDragSource(Entity entity)
    {
        if (!ImGui.BeginDragDropSource())
            return;
        long id = entity.LocalId;
        ImGui.SetDragDropPayload(EntityPayload, (IntPtr)(&id), sizeof(long));
        ImGui.TextUnformatted(entity.Name);
        ImGui.EndDragDropSource();
    }

    /// <summary>Accepts a dragged entity and parents it to <paramref name="newParentId"/> (0 = root).</summary>
    private unsafe void DrawDropTarget(long newParentId)
    {
        if (!ImGui.BeginDragDropTarget())
            return;

        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(EntityPayload);
        if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
        {
            long draggedId = Marshal.ReadInt64(payload.Data);
            if (Scene.Find(draggedId) is { } dragged && dragged.ParentId != newParentId &&
                Scene.CanReparent(dragged, newParentId))
            {
                _pendingChange = () => _context.Execute(new ReparentEntityCommand(Scene, dragged, newParentId));
            }
        }
        ImGui.EndDragDropTarget();
    }

    private void DrawContextMenu(Entity entity)
    {
        if (!ImGui.BeginPopupContextItem())
            return;

        _context.SelectedEntityId = entity.LocalId;
        if (ImGui.BeginMenu("Create Child"))
        {
            DrawCreateMenuItems(entity.LocalId);
            ImGui.EndMenu();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Rename", "F2"))
            BeginRename(entity);
        if (ImGui.MenuItem("Delete", "Del"))
            _pendingChange = () => Delete(entity);
        ImGui.EndPopup();
    }

    private void DrawCreateMenuItems(long parentId)
    {
        if (ImGui.MenuItem($"{Icons.Entity}  Empty"))
            _pendingChange = () => Create("Entity", parentId, withCube: false);
        if (ImGui.MenuItem($"{Icons.MeshRenderer}  Cube"))
            _pendingChange = () => Create("Cube", parentId, withCube: true);
    }

    /// <summary>Space below the tree: drop to unparent, click to deselect, right-click to create.</summary>
    private void DrawEmptyArea()
    {
        Vector2 size = ImGui.GetContentRegionAvail();
        size.Y = MathF.Max(size.Y, ImGui.GetFrameHeight());
        if (size.X < 1)
            return;

        ImGui.InvisibleButton("##hierarchyEmptyArea", size);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _context.SelectedEntityId = 0;
        DrawDropTarget(0);

        if (ImGui.BeginPopupContextItem("EmptyAreaContext"))
        {
            DrawCreateMenuItems(parentId: 0);
            ImGui.EndPopup();
        }
    }

    private void HandleShortcuts()
    {
        if (_renamingId != 0 || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;
        if (_context.SelectedEntity is not { } selected)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            _pendingChange = () => Delete(selected);
        else if (ImGui.IsKeyPressed(ImGuiKey.F2))
            BeginRename(selected);
    }

    private void BeginRename(Entity entity)
    {
        _context.SelectedEntityId = entity.LocalId;
        _renamingId = entity.LocalId;
        _renameBuffer = entity.Name;
        _focusRenameField = true;
    }

    private void DrawRenameField(Entity entity)
    {
        if (_focusRenameField)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRenameField = false;
        }

        // No vertical padding so the field fits in the tree row.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 1));
        ImGui.SetNextItemWidth(-1);
        bool submitted = ImGui.InputText("##rename", ref _renameBuffer, 256,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        ImGui.PopStyleVar();

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _renamingId = 0;
        }
        else if (submitted || ImGui.IsItemDeactivated())
        {
            string name = _renameBuffer.Trim();
            if (name.Length > 0 && name != entity.Name)
                _context.Execute(new RenameEntityCommand(entity, name));
            _renamingId = 0;
        }
    }

    private void Create(string name, long parentId, bool withCube)
    {
        var command = new CreateEntityCommand(Scene, name, parentId,
            withCube ? () => [new MeshRenderer()] : null);
        _context.Execute(command);
        _context.SelectedEntityId = command.Entity!.LocalId;
    }

    private void Delete(Entity entity)
    {
        _context.Execute(new DeleteEntityCommand(Scene, entity));
        if (_context.SelectedEntity == null)
            _context.SelectedEntityId = 0;
        if (Scene.Find(_renamingId) == null)
            _renamingId = 0;
    }
}
