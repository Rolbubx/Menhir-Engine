# Installer

`Menhir.Setup` is Menhir's installer and uninstaller: a console wizard (with the logo), no administrator
rights needed. It installs from the `Menhir.zip` next to it:

| In the installation folder | |
|-|-|
| `Editor/` | The editor, published self-contained (it carries its .NET runtime) |
| `Engine/` | The player's sources, the scripting API and GLFW/glad: File → Build compiles games from them |
| `Samples/Sandbox/` | The sample project, listed in the Hub |
| `Docs/`, `README.md` | |
| `toolchain.json` | The C compiler chosen during the installation (see `Editor/Menhir.Editor.Core/Build/Toolchain.cs`) |
| `Uninstall.exe` | A copy of the installer, which uninstalls when named so (or with `--uninstall`) |
| `install.json` | What was installed: the uninstaller removes exactly that, and leaves anything else in the folder |

The wizard asks:

1. **The folder**: by default `%LOCALAPPDATA%\Programs\Menhir` (no administrator rights needed).
2. **The C compiler**: it lists the GCC and Clang it finds (on the PATH, and in MSYS2, w64devkit,
   TDM-GCC, LLVM-MinGW, Scoop's usual folders) with their versions; type a number, a full path or a name on the
   PATH. It checks the compiler by compiling and running a small C program. MSVC (`cl.exe`) is not supported:
   the scripts and the player need GCC/Clang features. A project can still use another compiler (Project
   Settings → Build → Compiler; empty means the one chosen here).
3. **.NET**: nothing to do with the usual package, whose editor carries its runtime. A package made with
   `--framework-dependent` needs .NET 10: the installer looks for it (DOTNET_ROOT, the PATH, Program Files),
   asks for its folder otherwise, and sets DOTNET_ROOT for the user when it is somewhere Windows would not look.
4. **Shortcuts**: Start menu (yes by default) and desktop (no by default).

On Windows it registers Menhir in Settings → Apps (for the current user). Uninstalling removes the shortcuts,
that entry, DOTNET_ROOT if the installer set it, and the installed files; it asks before removing the sample
project and the editor's preferences (layout, recent projects). Projects made elsewhere are never touched.

Options, for scripted installs (`--yes` answers every open question with its default):

```
MenhirSetup.exe [--dir <folder>] [--compiler <path|name>] [--dotnet <folder>] [--no-start-menu] [--desktop]
                [--no-launch] [--payload <Menhir.zip>] [--yes]
Uninstall.exe [--yes]
```

## Making the package

```sh
Installer/package.sh            # win-x64 (from Linux, macOS or Windows' Git Bash; needs the .NET SDK and zip)
Installer/package.sh linux-x64
Installer/package.sh win-x64 --framework-dependent
```

It writes `artifacts/Menhir-Setup-<rid>/` (`MenhirSetup.exe` and `Menhir.zip`) and a zip of that folder. The
installer is not signed: Windows SmartScreen shows "Windows protected your PC" (More info → Run anyway), and
Smart App Control, when it is on, may refuse it.
