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
