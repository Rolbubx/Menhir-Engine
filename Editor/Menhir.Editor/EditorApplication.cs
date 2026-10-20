using System.Runtime.InteropServices;
using ImGuiNET;
using Menhir.Editor.Panels;
using Menhir.Editor.UI;
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
    private readonly List<EditorPanel> _panels;
    private readonly EditorDockspace _dockspace;

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;

    // Closing the window raises Closing synchronously, which tears down ImGui,
    // so a close requested from the UI is deferred until the frame is finished.
    private bool _closeRequested;

    // ImGui keeps a raw pointer to the ini path, so it must outlive the ImGui context.
    private IntPtr _layoutFilePath;

    public EditorApplication()
    {
        EditorPaths.EnsureConfigDirectory();
        _preferences = EditorPreferences.Load(EditorPaths.PreferencesFile);

        _panels =
        [
            new ViewportPanel(),
            new HierarchyPanel(),
            new InspectorPanel(),
            new ProjectPanel(),
            new ConsolePanel(),
        ];
        foreach (EditorPanel panel in _panels)
        {
            if (_preferences.OpenPanels.TryGetValue(panel.Title, out bool open))
                panel.IsOpen = open;
        }

        // No saved layout yet: build the default one on the first frame.
        _dockspace = new EditorDockspace(buildDefaultLayout: !File.Exists(EditorPaths.LayoutFile));

        WindowOptions options = WindowOptions.Default with
        {
            Title = "Menhir Editor",
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
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input, EditorFonts.CreateConfig(), ConfigureImGui);

        Console.WriteLine($"OpenGL {_gl.GetStringS(StringName.Version)} — {_gl.GetStringS(StringName.Renderer)}");
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

        _imgui.Render();

        if (_closeRequested)
            _window.Close();
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("File"))
        {
            ImGui.MenuItem("New Project...", null, false, false);
            ImGui.MenuItem("Open Project...", null, false, false);
            ImGui.Separator();
            ImGui.MenuItem("Save Scene", "Ctrl+S", false, false);
            ImGui.Separator();
            if (ImGui.MenuItem("Exit"))
                _closeRequested = true;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            ImGui.MenuItem("Undo", "Ctrl+Z", false, false);
            ImGui.MenuItem("Redo", "Ctrl+Y", false, false);
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

    private void OnClosing()
    {
        _preferences.WindowMaximized = _window.WindowState == WindowState.Maximized;
        if (_window.WindowState == WindowState.Normal)
        {
            _preferences.WindowWidth = _window.Size.X;
            _preferences.WindowHeight = _window.Size.Y;
        }
        foreach (EditorPanel panel in _panels)
            _preferences.OpenPanels[panel.Title] = panel.IsOpen;
        _preferences.Save(EditorPaths.PreferencesFile);

        // Destroying the ImGui context writes layout.ini; do it while the GL context is alive.
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
