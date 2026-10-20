using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Panels;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Rendering.OpenGL;
using Menhir.Editor.Assets;
using Menhir.Editor.Commands;
using Menhir.Editor.UI;
using Menhir.Editor.UI.Dialogs;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace Menhir.Editor;

/// <summary>
/// Owns the main window, the GL context and the ImGui frame loop.
/// </summary>
public sealed class EditorApplication : IDisposable
{
    private readonly IWindow _window;
    private readonly EditorPreferences _preferences;
    private readonly EditorContext _context = new();
    private readonly EditorDockspace _dockspace;
    private List<EditorPanel> _panels = [];

    private readonly UnsavedChangesDialog _unsavedChangesDialog = new();
    private readonly SaveSceneDialog _saveSceneDialog;
    private readonly OpenProjectDialog _openProjectDialog = new();
    private readonly NewProjectDialog _newProjectDialog = new();

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private IRenderer? _renderer;

    // Closing the window raises Closing synchronously, which tears down ImGui,
    // so a close requested from the UI is deferred until the frame is finished.
    private bool _closeRequested;

    // Set once the user chose "Don't Save" when closing, so the next close goes through.
    private bool _discardChangesOnClose;

    private string _title = "";

    // ImGui keeps a raw pointer to the ini path, so it must outlive the ImGui context.
    private IntPtr _layoutFilePath;

    public EditorApplication(string[] args)
    {
        EditorPaths.EnsureConfigDirectory();
        _preferences = EditorPreferences.Load(EditorPaths.PreferencesFile);
        _saveSceneDialog = new SaveSceneDialog(_context);
        OpenStartupProject(args);

        // No saved layout yet: build the default one on the first frame.
        _dockspace = new EditorDockspace(buildDefaultLayout: !File.Exists(EditorPaths.LayoutFile));

        WindowOptions options = WindowOptions.Default with
        {
            Title = BuildTitle(),
            Size = new Vector2D<int>(_preferences.WindowWidth, _preferences.WindowHeight),
            WindowState = _preferences.WindowMaximized ? WindowState.Maximized : WindowState.Normal,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            VSync = true,
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += size => _gl?.Viewport(size);
        _window.Closing += OnClosing;
        // Pick up files added, changed or removed outside the editor, like Unity does.
        _window.FocusChanged += focused =>
        {
            if (focused)
                _context.RefreshAssets();
        };
    }

    private void OpenStartupProject(string[] args)
    {
        string? path = ProjectLocator.Find(args, _preferences.RecentProjects);
        if (path == null)
        {
            Console.WriteLine("No project found; use File > Open Project or start with --project <folder>.");
            return;
        }
        try
        {
            OpenProject(Project.Open(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not open project '{path}': {e.Message}");
        }
    }

    private void OpenProject(Project project)
    {
        _context.OpenProject(project);
        _preferences.AddRecentProject(project.RootPath);
        _preferences.Save(EditorPaths.PreferencesFile);
        Console.WriteLine($"Opened project {project.RootPath}");
    }

    private string BuildTitle()
    {
        if (_context.Project is not { } project)
            return "Menhir Editor";
        string scene = _context.ActiveScene.Name + (_context.IsSceneDirty ? "*" : "");
        return $"Menhir Editor — {project.Name} — {scene}";
    }

    /// <summary>Runs <paramref name="action"/> once unsaved scene changes are saved or discarded.</summary>
    private void ConfirmDiscardChanges(Action action)
    {
        if (!_context.IsSceneDirty)
        {
            action();
            return;
        }
        _unsavedChangesDialog.Open(_context.ActiveScene.Name, onSave: () => SaveScene(then: action), onDiscard: action);
    }

    /// <summary>Saves to the scene's file, or asks for one when it was never saved.</summary>
    private void SaveScene(Action? then = null)
    {
        if (_context.Project == null)
            return;
        if (_context.ActiveScenePath == null)
        {
            _saveSceneDialog.Open(then);
            return;
        }
        try
        {
            _context.SaveScene();
            then?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save the scene: {e.Message}");
        }
    }

    private void OpenSceneAsset(AssetEntry asset) =>
        ConfirmDiscardChanges(() => _context.OpenScene(asset));

    private string DialogStartFolder =>
        _context.Project is { } project && Directory.GetParent(project.RootPath) is { } parent
            ? parent.FullName
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input, EditorFonts.CreateConfig(), ConfigureImGui);
        ImGuiModifierKeys.Attach(_input);

        Console.WriteLine($"OpenGL {_gl.GetStringS(StringName.Version)} — {_gl.GetStringS(StringName.Renderer)}");

        _renderer = new GLRenderer(_gl);
        _panels =
        [
            new ViewportPanel(_context, _renderer, SetCursorCaptured),
            new HierarchyPanel(_context),
            new InspectorPanel(_context),
            new ProjectPanel(_context, OpenSceneAsset),
            new ConsolePanel(),
        ];
        foreach (EditorPanel panel in _panels)
        {
            if (_preferences.OpenPanels.TryGetValue(panel.Title, out bool open))
                panel.IsOpen = open;
        }
    }

    /// <summary>Hides the cursor and gives unbounded mouse motion, e.g. while flying the viewport camera.</summary>
    private void SetCursorCaptured(bool captured)
    {
        foreach (IMouse mouse in _input!.Mice)
        {
            ICursor cursor = mouse.Cursor;
            if (!captured)
                cursor.CursorMode = CursorMode.Normal;
            else
                cursor.CursorMode = cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled;
        }
    }

    private unsafe void ConfigureImGui()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigWindowsMoveFromTitleBarOnly = true;

        _layoutFilePath = Marshal.StringToCoTaskMemUTF8(EditorPaths.LayoutFile);
        io.NativePtr->IniFilename = (byte*)_layoutFilePath;

        EditorTheme.Apply();
    }

    private void OnRender(double deltaTime)
    {
        _imgui!.Update((float)deltaTime);

        _gl!.ClearColor(0.08f, 0.08f, 0.08f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        DrawMainMenuBar();
        _dockspace.Draw();
        foreach (EditorPanel panel in _panels)
            panel.Draw();

        // Commands from one mouse drag or one text edit merge into one undo step.
        if (!ImGui.IsAnyItemActive())
            _context.History.EndMerge();

        _unsavedChangesDialog.Draw();
        _saveSceneDialog.Draw();
        _openProjectDialog.Draw();
        _newProjectDialog.Draw();
        HandleShortcuts();

        _imgui.Render();

        string title = BuildTitle();
        if (title != _title)
        {
            _title = title;
            _window.Title = title;
        }

        if (_closeRequested)
        {
            _closeRequested = false;
            _window.Close();
        }
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("File"))
        {
            DrawFileMenu();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            CommandHistory history = _context.History;
            if (ImGui.MenuItem(history.UndoName is { } undo ? $"Undo {undo}" : "Undo", "Ctrl+Z", false, history.CanUndo))
                history.Undo();
            if (ImGui.MenuItem(history.RedoName is { } redo ? $"Redo {redo}" : "Redo", "Ctrl+Y", false, history.CanRedo))
                history.Redo();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            foreach (EditorPanel panel in _panels)
            {
                bool open = panel.IsOpen;
                if (ImGui.MenuItem(panel.Title, null, ref open))
                    panel.IsOpen = open;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Reset Layout"))
            {
                foreach (EditorPanel panel in _panels)
                    panel.IsOpen = true;
                _dockspace.RequestLayoutReset();
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawFileMenu()
    {
        bool hasProject = _context.Project != null;

        if (ImGui.MenuItem("New Scene", "Ctrl+N", false, hasProject))
            ConfirmDiscardChanges(_context.NewScene);
        if (ImGui.BeginMenu("Open Scene", hasProject))
        {
            List<AssetEntry> scenes = _context.FindSceneAssets().ToList();
            foreach (AssetEntry scene in scenes)
            {
                if (ImGui.MenuItem(scene.Path, null, scene.Guid == _context.ActiveSceneGuid))
                    OpenSceneAsset(scene);
            }
            if (scenes.Count == 0)
                ImGui.MenuItem("No scenes in this project", null, false, false);
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem("Save Scene", "Ctrl+S", false, hasProject))
            SaveScene();
        if (ImGui.MenuItem("Save Scene As...", "Ctrl+Shift+S", false, hasProject))
            _saveSceneDialog.Open();

        ImGui.Separator();
        if (ImGui.MenuItem("New Project..."))
            ConfirmDiscardChanges(() => _newProjectDialog.Open(DialogStartFolder, OpenProject));
        if (ImGui.MenuItem("Open Project..."))
            ConfirmDiscardChanges(() => _openProjectDialog.Open(DialogStartFolder, OpenProject));
        if (ImGui.BeginMenu("Open Recent", _preferences.RecentProjects.Count > 0))
        {
            foreach (string path in _preferences.RecentProjects.ToList())
            {
                bool exists = Project.IsProjectFolder(path);
                if (ImGui.MenuItem(path, null, path == _context.Project?.RootPath, exists))
                    ConfirmDiscardChanges(() => OpenProject(Project.Open(path)));
            }
            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Exit"))
            RequestExit();
    }

    /// <summary>
    /// Silk's Close() cannot be cancelled, so unsaved changes are handled before calling it.
    /// (The OS close button goes through OnClosing instead.)
    /// </summary>
    private void RequestExit() =>
        ConfirmDiscardChanges(() =>
        {
            _discardChangesOnClose = true;
            _closeRequested = true;
        });

    private void HandleShortcuts()
    {
        if (ModalDialog.AnyOpen)
            return;

        // A focused text field has its own undo (Ctrl+Z inside the text).
        if (!ImGui.GetIO().WantTextInput)
        {
            if (EditorInput.Shortcut(ImGuiKey.Z))
                _context.History.Undo();
            else if (EditorInput.Shortcut(ImGuiKey.Y) || EditorInput.Shortcut(ImGuiKey.Z, shift: true))
                _context.History.Redo();
        }

        if (_context.Project == null)
            return;

        if (EditorInput.Shortcut(ImGuiKey.S))
            SaveScene();
        else if (EditorInput.Shortcut(ImGuiKey.S, shift: true))
            _saveSceneDialog.Open();
        else if (EditorInput.Shortcut(ImGuiKey.N))
            ConfirmDiscardChanges(_context.NewScene);
    }

    private void OnClosing()
    {
        // The OS close button can be cancelled: keep the window open and ask about unsaved changes.
        if (_context.IsSceneDirty && !_discardChangesOnClose)
        {
            _window.IsClosing = false;
            RequestExit();
            return;
        }

        _preferences.WindowMaximized = _window.WindowState == WindowState.Maximized;
        if (_window.WindowState == WindowState.Normal)
        {
            _preferences.WindowWidth = _window.Size.X;
            _preferences.WindowHeight = _window.Size.Y;
        }
        foreach (EditorPanel panel in _panels)
            _preferences.OpenPanels[panel.Title] = panel.IsOpen;
        _preferences.Save(EditorPaths.PreferencesFile);

        // GPU resources and the ImGui context (whose destruction writes layout.ini)
        // must go while the GL context is still alive.
        foreach (IDisposable disposable in _panels.OfType<IDisposable>())
            disposable.Dispose();
        _renderer?.Dispose();
        _renderer = null;
        _imgui?.Dispose();
        _imgui = null;
        _input?.Dispose();
        _input = null;
        _gl?.Dispose();
        _gl = null;
    }

    public void Dispose()
    {
        _window.Dispose();
        if (_layoutFilePath != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_layoutFilePath);
            _layoutFilePath = IntPtr.Zero;
        }
    }
}
