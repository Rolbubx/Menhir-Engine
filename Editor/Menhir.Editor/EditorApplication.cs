using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Panels;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Rendering.OpenGL;
using Menhir.Editor.Assets;
using Menhir.Editor.Building;
using Menhir.Editor.Playing;
using Menhir.Editor.Scripting;
using Menhir.Editor.Commands;
using Menhir.Editor.Logging;
using Menhir.Editor.UI;
using Menhir.Editor.UI.Dialogs;
using Menhir.Editor.UI.Hub;
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
    private readonly StatusBar _statusBar;
    private List<EditorPanel> _panels = [];

    private readonly UnsavedChangesDialog _unsavedChangesDialog = new();
    private readonly SaveSceneDialog _saveSceneDialog;
    private readonly ProjectHub _hub;
    private readonly BuildRunner _build;
    private readonly ScriptManager _scripts = new();
    private readonly PlayMode _play;
    private GamePanel? _gamePanel;
    private bool _wasPlaying;

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

    // The layout is loaded and saved only once a project is open: frames showing only the hub
    // do not submit the dockspace, and saving then would drop the docked panels from layout.ini.
    private bool _layoutEnabled;

    public EditorApplication(string[] args)
    {
        EditorPaths.EnsureConfigDirectory();
        _preferences = EditorPreferences.Load(EditorPaths.PreferencesFile);
        _saveSceneDialog = new SaveSceneDialog(_context);
        _build = new BuildRunner(_context);
        _play = new PlayMode(_context, _scripts);
        _statusBar = new StatusBar(Log.Messages, ShowConsole);
        _hub = new ProjectHub(_preferences, OpenProject, ProjectLocator.FindSample());
        OpenStartupProject(args);

        if (_preferences.LayoutVersion != EditorDockspace.LayoutVersion)
        {
            File.Delete(EditorPaths.LayoutFile);
            _preferences.LayoutVersion = EditorDockspace.LayoutVersion;
        }

        _dockspace = new EditorDockspace();

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

    /// <summary>Opens the project given with --project; otherwise the hub is shown.</summary>
    private void OpenStartupProject(string[] args)
    {
        string? path = ProjectLocator.FromArgs(args);
        if (path == null)
            return;
        try
        {
            OpenProject(Project.Open(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not open project '{path}': {e.Message}");
        }
    }

    private void OpenProject(Project project)
    {
        Log.Info($"Opened project {project.RootPath}");
        if (project.Settings.Remote is { } remote)
            Log.Warning($"This project has a remote ({remote.Url}), but remote collaboration is not available yet: it is opened as a local project.");
        _play.Stop();
        _context.OpenProject(project);
        _scripts.OpenProject(project);
        _preferences.AddRecentProject(project.RootPath);
        _preferences.Save(EditorPaths.PreferencesFile);
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
            Log.Error($"Could not save the scene: {e.Message}");
        }
    }

    private void OpenSceneAsset(AssetEntry asset) =>
        ConfirmDiscardChanges(() => _context.OpenScene(asset));

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        // Fonts are added in ConfigureImGui, which runs before the controller builds the font atlas.
        _imgui = new ImGuiController(_gl, _window, _input, null, ConfigureImGui);
        ImGuiModifierKeys.Attach(_input);

        Log.Info($"OpenGL {_gl.GetStringS(StringName.Version)} — {_gl.GetStringS(StringName.Renderer)}");

        _renderer = new GLRenderer(_gl);
        _gamePanel = new GamePanel(_context, _play, _renderer, _input, SetCursorCaptured);
        _panels =
        [
            new ViewportPanel(_context, _renderer, SetCursorCaptured, () => _play.Scenes),
            _gamePanel,
            new HierarchyPanel(_context),
            new InspectorPanel(_context, _scripts),
            new ProjectPanel(_context, OpenSceneAsset) { GridView = _preferences.ProjectGridView },
            new ConsolePanel(Log.Messages, _preferences.ConsoleFilter),
            new ProjectSettingsPanel(_context),
        ];
        foreach (EditorPanel panel in _panels)
            panel.IsOpen = _preferences.OpenPanels.TryGetValue(panel.Title, out bool open) ? open : panel.OpenByDefault;
    }

    private void ShowConsole() => ShowPanel<ConsolePanel>();

    private void ShowPanel<T>() where T : EditorPanel
    {
        EditorPanel panel = _panels.OfType<T>().First();
        panel.IsOpen = true;
        ImGui.SetWindowFocus(panel.WindowId);
    }

    /// <summary>
    /// Starts loading and saving layout.ini. Without a saved layout, the default one is built on the next frame.
    /// Must be called outside of an ImGui frame.
    /// </summary>
    private unsafe void EnableLayout(bool loadNow)
    {
        if (!File.Exists(EditorPaths.LayoutFile))
            _dockspace.RequestLayoutReset();
        else if (loadNow)
            ImGui.LoadIniSettingsFromDisk(EditorPaths.LayoutFile);
        ImGui.GetIO().NativePtr->IniFilename = (byte*)_layoutFilePath;
        _layoutEnabled = true;
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
        io.NativePtr->IniFilename = null;
        if (_context.Project != null)
            EnableLayout(loadNow: false); // ImGui loads the file itself on its first frame

        EditorFonts.Load();
        EditorTheme.Apply();
    }

    private void OnRender(double deltaTime)
    {
        _imgui!.Update((float)deltaTime);

        _gl!.ClearColor(0.08f, 0.08f, 0.08f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Without a project, the hub fills the window.
        if (_context.Project == null)
        {
            _hub.DrawFullscreen();
            FinishFrame();
            return;
        }

        // The game's frame runs before the editor's panels draw it.
        _scripts.Update();
        _play.Update(_gamePanel!.ReadInput(), (float)deltaTime);
        if (_play.IsPlaying && !_wasPlaying)
            _gamePanel.Focus();
        _wasPlaying = _play.IsPlaying;

        DrawMainMenuBar();
        _dockspace.Draw(bottomMargin: StatusBar.Height);
        _statusBar.Draw();
        foreach (EditorPanel panel in _panels)
            panel.Draw();

        // Commands from one mouse drag or one text edit merge into one undo step.
        if (!ImGui.IsAnyItemActive())
            _context.History.EndMerge();

        _unsavedChangesDialog.Draw();
        _saveSceneDialog.Draw();
        _hub.DrawModal();
        HandleShortcuts();
        FinishFrame();
    }

    private void FinishFrame()
    {
        _imgui!.Render();

        // A project was just opened from the hub: load the layout between two frames.
        if (_context.Project != null && !_layoutEnabled)
            EnableLayout(loadNow: true);

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
        // Tinted in Play mode, like Unity, so edits made while playing are not mistaken for the game's.
        bool tinted = _play.IsPlaying;
        if (tinted)
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, EditorTheme.PlayModeTint);
        bool menuBar = ImGui.BeginMainMenuBar();
        if (tinted)
            ImGui.PopStyleColor();
        if (!menuBar)
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
            ImGui.Separator();
            if (ImGui.MenuItem("Project Settings..."))
                ShowPanel<ProjectSettingsPanel>();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            foreach (EditorPanel panel in _panels)
            {
                bool open = panel.IsOpen;
                if (ImGui.MenuItem($"{panel.Icon}  {panel.Title}", null, ref open))
                    panel.IsOpen = open;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Reset Layout"))
            {
                foreach (EditorPanel panel in _panels)
                    panel.IsOpen = panel.OpenByDefault;
                _dockspace.RequestLayoutReset();
            }
            ImGui.EndMenu();
        }

        DrawPlayButtons();
        ImGui.EndMainMenuBar();
    }

    /// <summary>Play, Pause and Step, in the middle of the menu bar.</summary>
    private void DrawPlayButtons()
    {
        float button = ImGui.GetFrameHeight() + 6;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float width = button * 3 + spacing * 2;
        float x = (ImGui.GetWindowWidth() - width) / 2;
        if (x < ImGui.GetCursorPosX())
            return; // no room left of the menus
        ImGui.SetCursorPosX(x);

        bool playing = _play.IsPlaying || _play.IsWaitingForScripts;
        if (PlayButton(playing ? Icons.Stop : Icons.Play, playing, button, "##play"))
            _play.TogglePlay();
        Tooltip(playing ? "Stop (Ctrl+P)" : _scripts.IsCompiling ? "Play (once the scripts are compiled) (Ctrl+P)" : "Play (Ctrl+P)");
        ImGui.SameLine();
        ImGui.BeginDisabled(!_play.IsPlaying);
        if (PlayButton(Icons.Pause, _play.IsPaused, button, "##pause"))
            _play.IsPaused = !_play.IsPaused;
        Tooltip("Pause (Ctrl+Shift+P)");
        ImGui.SameLine();
        if (PlayButton(Icons.Step, false, button, "##step"))
            _play.Step();
        Tooltip("Step one frame");
        ImGui.EndDisabled();
    }

    private static bool PlayButton(string icon, bool active, float width, string id)
    {
        if (active)
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Accent);
        bool clicked = ImGui.Button(icon + id, new System.Numerics.Vector2(width, 0));
        if (active)
            ImGui.PopStyleColor();
        return clicked;
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
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
        bool canBuild = hasProject && !_build.IsBuilding;
        if (ImGui.MenuItem(_build.IsBuilding ? "Building..." : "Build", "Ctrl+B", false, canBuild))
            _build.Start(run: false);
        if (ImGui.MenuItem("Build And Run", "Ctrl+Shift+B", false, canBuild))
            _build.Start(run: true);
        if (ImGui.MenuItem("Build Settings...", null, false, hasProject))
            ShowPanel<ProjectSettingsPanel>();

        ImGui.Separator();
        if (ImGui.MenuItem("New Project..."))
            ConfirmDiscardChanges(() => _hub.Show(ProjectHub.Page.NewProject));
        if (ImGui.MenuItem("Open Project..."))
            ConfirmDiscardChanges(() => _hub.Show(ProjectHub.Page.Projects));
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
        else if (EditorInput.Shortcut(ImGuiKey.B))
            _build.Start(run: false);
        else if (EditorInput.Shortcut(ImGuiKey.B, shift: true))
            _build.Start(run: true);
        else if (EditorInput.Shortcut(ImGuiKey.P))
            _play.TogglePlay();
        else if (EditorInput.Shortcut(ImGuiKey.P, shift: true) && _play.IsPlaying)
            _play.IsPaused = !_play.IsPaused;
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
        if (_panels.OfType<ProjectPanel>().FirstOrDefault() is { } project)
            _preferences.ProjectGridView = project.GridView;
        _preferences.Save(EditorPaths.PreferencesFile);

        // GPU resources and the ImGui context (whose destruction writes layout.ini)
        // must go while the GL context is still alive.
        _play.Dispose();
        foreach (IDisposable disposable in _panels.OfType<IDisposable>())
            disposable.Dispose();
        _scripts.Dispose();
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
