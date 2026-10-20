# Menhir Engine

A personal game engine. Games are scripted in C#; the editor runs scripts directly on .NET,
and a future IL→C translator plus a native C runtime will produce the final builds.

| Folder        | What                                                      |
|---------------|-----------------------------------------------------------|
| `Editor/`     | C# editor (Silk.NET + OpenGL 3.3 Core + ImGui docking)    |
| `Engine/`     | Future native C runtime                                   |
| `Translator/` | Future IL2C translator                                    |
| `Samples/`    | Sample projects for testing the editor                    |

## Running the editor

Requires the .NET 10 SDK.

```sh
dotnet run --project Editor/Menhir.Editor
```

The window layout and editor preferences are stored per user, outside the repo:
`%APPDATA%/Menhir/Editor` on Windows, `~/.config/Menhir/Editor` on Linux.
Use **Window → Reset Layout** to restore the default layout.

Run the unit tests with `dotnet test`.

### Viewport controls

| Input                    | Action                    |
|--------------------------|---------------------------|
| Hold right mouse button  | Fly mode (mouse to look)  |
| W / A / S / D            | Move forward / left / back / right |
| Q / E                    | Move down / up            |
| Shift                    | Move 3× faster            |
| Mouse wheel              | Change fly speed          |

### Hierarchy

- Click to select (the selection is outlined in orange in the viewport); click the empty area to deselect
- **+** button or right-click: create an empty entity or a cube (as a child from an entity's menu)
- Double-click or **F2** to rename (**Enter** to confirm, **Esc** to cancel); **Del** deletes with children
- Drag an entity onto another to reparent it (its world position is kept); drop it in the empty area to make it a root

### Inspector

- Edit the selected entity's name (applied when the field loses focus) and its Transform
- Rotation is shown as Euler angles in degrees, applied Z, then X, then Y (like Unity)
- Drag a number to change it, or Ctrl+click / double-click it to type a value
- Components are drawn by reflection: every public property with a public getter and setter is shown,
  unless marked `[HideInInspector]`; a `Vector4` marked `[Color]` gets a color picker

World conventions: right-handed, Y up, 1 unit = 1 meter.

The editor font is [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono), distributed under the
SIL Open Font License (`Editor/Menhir.Editor/Resources/Fonts/JetBrainsMono-OFL.txt`).
