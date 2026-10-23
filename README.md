# Menhir Engine

A personal game engine with a Unity-like editor in C#. Games are scripted in C or C++
([Docs/Scripting.md](Docs/Scripting.md)): the editor's Play mode compiles the scripts into a library it
loads, and **File → Build** makes a native game: one `main.c` (OpenGL + GLFW, the same rendering as the
editor) compiled with the scripts, and its `GameData.binchunk` ([Engine/README](Engine/README.md)).

| Folder        | What                                                      |
|---------------|-----------------------------------------------------------|
| `Editor/`     | C# editor (Silk.NET + OpenGL 3.3 Core + ImGui docking)    |
| `Engine/`     | Native C side: game player, scripting API, GLFW, glad ([README](Engine/README.md)) |
| `Samples/`    | Sample projects for testing the editor                    |
| `Installer/`  | Installers: Windows (Inno Setup) and console ([README](Installer/README.md)) |

## Installing

On Windows, run `Menhir-Setup-win-x64.exe` from the latest GitHub release: a usual Windows installer (Inno
Setup) that asks where to install Menhir and which C compiler (GCC or Clang) to use, adds it to the Start menu,
and registers an uninstaller in Settings → Apps. Linux has a console installer (`Menhir-Setup-linux-x64.zip`);
see [Installer/README](Installer/README.md).
**File → Update Editor** then updates it from the latest GitHub release and restarts on the open project.

## Running the editor from the sources

Requires the .NET 10 SDK, and GCC or Clang for the scripts and builds.

```sh
dotnet run --project Editor/Menhir.Editor
```

The editor starts on the **Hub**:

- **Projects**: click a recent project to open it (right-click to remove it from the list); the
  `Samples/Sandbox` project is listed when the editor runs from this repository. Or open a project
  folder (**Project's dir**, one that contains `Assets/`). Opening from a remote is not available yet.
- **New Project**: project type (for now, *Menhir Universal Pipeline*), name, the folder to create it in,
  and an optional remote (`http://...`). With a remote, choose what happens to collaborators when you quit
  and how files are sent to them. These choices are saved in `ProjectSettings/ProjectSettings.json`;
  remote collaboration itself is not implemented yet (see [Docs/ProjectRemote.md](Docs/ProjectRemote.md)).

**File → New Project / Open Project** opens the Hub again; **Open Recent** switches directly.
A project folder can also be given on the command line, which skips the Hub:

```sh
dotnet run --project Editor/Menhir.Editor -- --project path/to/MyGame
```

The window layout and editor preferences are stored per user, outside the repo:
`%APPDATA%/Menhir/Editor` on Windows, `~/.config/Menhir/Editor` on Linux.
Use **Window → Reset Layout** to restore the default layout.

Run the unit tests with `dotnet test`.

### Projects and scenes

A project is a folder with `Assets/` (committed, with the `.meta` files), `ProjectSettings/`
(committed) and `Cache/` (git-ignored; e.g. which scene to reopen). **New Project** creates that
layout plus `Assets/Scenes/Main.scene` (a camera, a sun, the sky and a global volume) and a `.gitignore`
(`Cache/` and `Build/`, where File → Build puts the game). The editor reopens a project's
last scene.

| Shortcut       | Action                                    |
|----------------|-------------------------------------------|
| Ctrl+N         | New scene                                 |
| Ctrl+S         | Save scene (asks for a name the first time) |
| Ctrl+Shift+S   | Save scene as                             |
| Ctrl+P         | Play / stop (Play mode)                   |
| Ctrl+Shift+P   | Pause / resume                            |
| Ctrl+B         | Build the game (File → Build)             |
| Ctrl+Shift+B   | Build and run the game                    |
| Ctrl+Z         | Undo                                      |
| Ctrl+Y / Ctrl+Shift+Z | Redo                               |

Every scene edit (create, delete, rename, reparent, Transform and component changes) is an undoable
command; a whole slider drag or text edit is one undo step. The history is per scene and is cleared
when another scene is opened. Asset operations in the Project panel (rename, move, delete) are not
undoable. While a text field is focused, Ctrl+Z undoes the typing in that field instead.

Open a scene from **File → Open Scene** or by double-clicking it in the Project panel. The editor
asks before discarding unsaved changes (also when closing the window).

Scenes are JSON files listing the entities in order, each with its local ID, parent ID, name,
Transform and components (saved by reflection from their public read/write properties).
A component whose type no longer exists is kept as is and saved back unchanged.

```json
{
  "version": 1,
  "nextLocalId": 2,
  "entities": [
    {
      "id": 1,
      "parent": 0,
      "name": "Cube",
      "transform": {
        "position": [0, 0.5, 0],
        "rotation": [0, 0, 0, 1],
        "scale": [1, 1, 1]
      },
      "components": [
        { "type": "MeshRenderer", "mesh": "Cube", "color": [0.5, 0.5, 0.5, 1] }
      ]
    }
  ]
}
```

### Viewport controls

| Input                    | Action                    |
|--------------------------|---------------------------|
| Hold right mouse button  | Fly mode (mouse to look)  |
| W / A / S / D            | Move forward / left / back / right |
| Q / E                    | Move down / up            |
| Shift                    | Move 3× faster            |
| Mouse wheel              | Change fly speed          |

**Effects** (top right of the viewport) shows or hides the skybox, shadows, post-processing and the grid.

### Scripts and Play mode

Every `.c` and `.cpp` file in `Assets/` is part of the project's scripts. In C++ a script is a class in the
style of Unity (`class Player : public MenhirEngine { void OnUpdate(MenhirTick dt) { ... } };`); in the simple
C syntax a file is one script named after it (`MH_VARS { ... }`, `MH_START() { ... }`...); the classic C one
declares a struct with `MH_SCRIPT`. Scripts are added to entities (**Add Component → Scripts**), and their fields show in
the Inspector and are saved with the scene. **Add Component** also has the built-in **Physics** (Box Collider,
Sphere Collider, Rigidbody) and **Audio** (Audio Source) components. **Project → Create → C++ Script** (or **C Script**) makes a
script from a template (renaming the new file renames it); double-click opens it with the system's editor. The
editor compiles the scripts in the background with the C compiler of **Project Settings → Build** (when the
project opens and whenever a `.c` or `.h` file changes) and lists the errors in the Console. Scripts can spawn
and destroy objects, tween them, use timers, coroutines, physics and sounds: see
[Docs/Scripting.md](Docs/Scripting.md).

The **Play** button (top middle, Ctrl+P) runs the game in the editor on copies of the scenes, starting with the
open one: the **Game** panel shows the game's camera and gets the keyboard, mouse and gamepad while it has the
focus (**Esc** frees a captured cursor); the Viewport shows the running game too. **Pause** and **Step** (one
frame) are next to it. Stopping goes back to the scenes as they were: the game never changes them, and edits
made while playing stay. Play mode waits for the scripts to compile, and does not start while they have errors.
The scripts run inside the editor: a script that crashes (a bad pointer) closes the editor, so save first.
On Windows, Smart App Control may refuse to load the compiled scripts (it blocks libraries it does not know).

### Rendering

The viewport renders like URP: scenes are lit and drawn in HDR, then post-processed.

- **Light** component: directional, point or spot. The first directional light is the sun (it also
  places the sun in the sky); up to 8 point / spot lights light each object. Lights shine along the
  entity's forward axis (-Z).
- **Shadows**, like URP: set a light's **Shadows** to Hard or Soft (and its strength). The sun uses
  cascaded shadow maps (1 to 4 cascades, up to Max Distance, fading out at the end); spot lights get
  one shadow map and point lights six (cube faces), sharing an atlas of 16 slots. Mesh Renderers can
  turn off **Cast Shadows** / **Receive Shadows**. Resolutions, distance, cascade splits, depth / normal
  bias and soft shadow quality are in **Edit → Project Settings** (saved in `ProjectSettings.json`).
- **Mesh Renderer**: a built-in cube or an imported **Model**, with base color, metallic and smoothness,
  shaded with URP Lit's BRDF.
- **Models**: `.obj` and glTF 2.0 (`.gltf` with embedded or external buffers, `.glb`) files in `Assets/` are
  imported when used (and again when the file changes): all their meshes, placed by their nodes, become one
  mesh. Missing normals are generated, smoothing edges under 60° like Unity. Materials, UVs, skins and
  animations are ignored for now; FBX and compressed glTF (Draco, meshopt) are not supported. Drag a model
  from the Project panel onto the Hierarchy to create an entity that draws it.
- **Procedural Skybox** component: Unity's Skybox/Procedural (Rayleigh and Mie scattering), with the
  same settings and defaults (sun disk, sun size and convergence, atmosphere thickness, sky tint, ground
  color, exposure). The sky also gives the ambient light. Scenes without one use the defaults.
- **Post-processing**: URP's volume overrides are components (Add Component → Post-processing),
  usually on a "Global Volume" entity; the checkbox in a component's header turns it off:
  Bloom, Channel Mixer, Chromatic Aberration, Color Adjustments, Color Curves, Color Lookup,
  Depth Of Field (Gaussian and Bokeh), Film Grain, Lens Distortion, Lift Gamma Gain, Motion Blur
  (camera), Panini Projection, Shadows Midtones Highlights, Split Toning, Tonemapping (Neutral, ACES),
  Vignette, White Balance. Settings and defaults follow URP.
- **Input**: an Input Manager like Unity's in **Edit → Project Settings → Input** (keys, mouse, gamepad), and
  two built-in **Gameplay** components that use it in the built game: **Fly Camera** and **Scene Switcher**
  (loads another scene of the build, alone or additively). See [Docs/Input.md](Docs/Input.md).
- **Camera** component: where the built game renders from (field of view, clip planes, skybox or solid
  color background). The viewport keeps its own camera.
- New scenes start like a URP scene: a Main Camera, a Directional Light with soft shadows, an Environment (the sky) and a Global Volume
  with ACES tonemapping, a little bloom and a vignette.
- Color Lookup takes a PNG strip of N tiles of N×N (e.g. 1024×32); `Samples/Sandbox/Assets/Textures/NeutralLUT32.png`
  is a neutral one to grade in an image editor.

Differences with URP: one global volume (the first active effect of each kind is used, no blending
or local volumes), no Bloom dirt texture, generated film grain textures instead of Unity's, ACES uses
Stephen Hill's fit of the RRT + ODT, and the master / RGB curves apply to the display range after
tonemapping.

### Hierarchy

- Click to select (the selection is outlined in orange in the viewport); click the empty area to deselect
- **+** button or right-click: create an empty entity or a cube (as a child from an entity's menu)
- Double-click or **F2** to rename (**Enter** to confirm, **Esc** to cancel); **Del** deletes with children
- Drag an entity onto another to reparent it (its world position is kept); drop it in the empty area to make it a root
- **Search** shows every entity whose name contains the text, as a flat list

### Inspector

- Edit the selected entity's name (applied when the field loses focus) and its Transform
- Rotation is shown as Euler angles in degrees, applied Z, then X, then Y (like Unity)
- Drag a number to change it, or Ctrl+click / double-click it to type a value
- Components are drawn by reflection: every public property with a public getter and setter is shown,
  unless marked `[HideInInspector]`. Attributes pick the widget: `[Color]` (color bar and picker),
  `[Range(min, max)]` (slider), `[Min]`, `[Trackball]` (tint + offset), `[TextureAsset]` (texture picker),
  `[Tooltip]`; color curves get a small curve editor (drag points, double-click to add, right-click to remove)
- **Add Component** (under the components) lists the component types, with a search field;
  the **⋮** button or a right-click on a component's header removes it (both can be undone)

### Project

- Folder tree on the left, content of the current folder on the right (as icons or as a list, toggled at the
  top right); double-click a folder to open it
- **Search** finds assets by name in the whole project
- Every file and folder in `Assets/` gets a `.meta` file holding its GUID and import settings.
  Commit them with the assets: references use the GUID, so an asset keeps its identity when renamed or moved
- Missing `.meta` files are created, orphan ones are deleted and copied ones get a new GUID when
  the project opens, when the editor window gets the focus back, or on **Refresh**
- **+** or right-click to create a folder; **F2** to rename, **Del** to delete (asks first, cannot be undone),
  drag onto a folder (in either pane or in the path bar) to move. Do these in the editor so the `.meta` follows;
  outside the editor, move the `.meta` along with its file
- Ignored: hidden files (`.name`), backups (`name~`) and `*.tmp`

### Console

- Editor messages (scenes opened and saved, asset refresh, warnings while loading a scene, errors),
  also written to stdout / stderr
- Toggle info, warnings and errors with the counters on the right; **Search** filters by text;
  **Collapse** shows repeated messages once with a count
- Select a message to see all of it below the list (drag the line in between to resize);
  **Copy** in the details, right-click, or Ctrl+C copies it
- Keeps the last 5000 messages; the filters are saved with the editor preferences

The status bar at the bottom of the window shows the latest message; click it to open the Console.

Editor code logs with `Log.Info`, `Log.Warning` and `Log.Error` (`Menhir.Editor.Logging`, safe from any thread).

World conventions: right-handed, Y up, 1 unit = 1 meter.

Fonts and icons, in `Editor/Menhir.Editor/Resources/Fonts` with their licenses:
[Inter](https://rsms.me/inter/) for the UI and [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono)
for logs, paths and GUIDs (both SIL Open Font License), and [Lucide](https://lucide.dev) icons (ISC License).
