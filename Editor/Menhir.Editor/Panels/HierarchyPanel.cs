using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Scenes;

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
    private bool _focusRenameField;

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public override string Title => "Hierarchy";

    private Scene Scene => _context.ActiveScene;

    protected override void DrawContent()
    {
        if (ImGui.Button("+"))
            ImGui.OpenPopup("CreateEntity");
        if (ImGui.BeginPopup("CreateEntity"))
        {
            DrawCreateMenuItems(parentId: 0);
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(Scene.Name);
        ImGui.Separator();

        foreach (Entity root in Scene.GetChildren(0))
            DrawEntityNode(root);

        DrawEmptyArea();
        HandleShortcuts();

        _pendingChange?.Invoke();
        _pendingChange = null;
    }

    private void DrawEntityNode(Entity entity)
    {
        bool hasChildren = Scene.HasChildren(entity);
        bool renaming = entity.LocalId == _renamingId;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth |
                                   ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (entity.LocalId == _context.SelectedEntityId)
            flags |= ImGuiTreeNodeFlags.Selected;

        // The ID after ## stays stable when the entity is renamed, so the open state survives.
        string label = renaming ? "" : entity.Name;
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
                _pendingChange = () => Scene.SetParent(dragged, newParentId);
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
        if (ImGui.MenuItem("Empty"))
            _pendingChange = () => Create("Entity", parentId, withCube: false);
        if (ImGui.MenuItem("Cube"))
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
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 0));
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
            if (name.Length > 0)
                entity.Name = name;
            _renamingId = 0;
        }
    }

    private void Create(string name, long parentId, bool withCube)
    {
        Entity entity = Scene.CreateEntity(name, parentId);
        if (withCube)
            entity.Components.Add(new MeshRenderer());
        _context.SelectedEntityId = entity.LocalId;
    }

    private void Delete(Entity entity)
    {
        Scene.DestroyEntity(entity);
        if (_context.SelectedEntity == null)
            _context.SelectedEntityId = 0;
        if (Scene.Find(_renamingId) == null)
            _renamingId = 0;
    }
}
