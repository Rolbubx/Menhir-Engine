# Installer

Menhir has two installers, made by `Installer/package.sh`:

- **Windows**: `Menhir-Setup-win-x64.exe`, a usual Windows installer made with [Inno Setup 6](https://jrsoftware.org/isinfo.php)
  from `Windows/Menhir.iss` (with its icon and wizard pictures).
- **Other platforms** (and Windows with `--zip`): `Menhir-Setup-<rid>.zip`, with `Menhir.Setup`, a console
  wizard with the logo.

## Windows installer

Its wizard asks:

1. **For whom**: just this user (recommended, no administrator rights, in `%LOCALAPPDATA%\Programs\Menhir`)
   or all users (in Program Files).
2. **The folder**.
3. **The C compiler**: it lists the GCC and Clang it finds (on the PATH, and in MSYS2, w64devkit, TDM-GCC,
   LLVM-MinGW, Scoop's usual folders); pick one, type a path or a name on the PATH, or browse. It checks that
   `<compiler> --version` runs and asks before keeping one that does not. MSVC (`cl.exe`) is not supported.
   The choice goes into `toolchain.json`; a project can still use another one (Project Settings → Build).
4. **A desktop shortcut** (no by default); the Start menu one is always made.

The editor is self-contained, so .NET is not needed. Menhir is registered in Settings → Apps; uninstalling
removes the shortcuts, `Editor/`, `Engine/` and the installed files, and asks before removing the editor's
preferences (layout, recent projects). Projects made elsewhere are never touched; the sample's own changes are
kept (it is only installed when missing).

Scripted installs take Inno Setup's usual options, and `/COMPILER=<path|name>`:

```
Menhir-Setup-win-x64.exe /VERYSILENT /CURRENTUSER /DIR=<folder> /COMPILER=gcc
```

## Console installer

`Menhir.Setup` is a console wizard (with the logo), no administrator rights needed. It installs from the
`Menhir.zip` next to it:

| In the installation folder | |
|-|-|
| `Editor/` | The editor, published self-contained (it carries its .NET runtime) |
| `Engine/` | The player's sources, the scripting API and GLFW/glad: File → Build compiles games from them |
| `Samples/Sandbox/` | The sample project, listed in the Hub |
| `Docs/`, `README.md` | |
| `toolchain.json` | The C compiler chosen during the installation (see `Editor/Menhir.Editor.Core/Build/Toolchain.cs`) |
| `Uninstall.exe` | A copy of the installer, which uninstalls when named so (or with `--uninstall`) |
| `install.json` | What was installed: the uninstaller removes exactly that, and leaves anything else in the folder |

The console wizard asks:

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
MenhirSetup.exe --update [--dir <folder>] [--wait-pid <editor's process id>] [--relaunch-project <folder>]
Uninstall.exe [--yes]
```

## Updates

**File > Update Editor** looks for the latest release on GitHub (also checked quietly when the editor starts:
the menu then says a new version is out). It shows what is new; **Update and Restart** asks about unsaved
changes, downloads the installer for this platform and starts it, then the editor closes:

- On Windows, `Menhir-Setup-win-x64.exe /SILENT /CLOSEAPPLICATIONS /DIR=<installation> /RELAUNCH=<project>`:
  only its progress window, keeping the compiler and the shortcuts, then the editor starts on the project.
- Elsewhere, `MenhirSetup --update`: it waits for the editor to close, replaces the installation (keeping the
  compiler, the shortcuts and the user's changes to the sample) and starts the editor again on the project.

An editor run from the sources only says what is new (update those with `git pull`).

Releases come from `.github/workflows/release.yml`: pushing a tag `vX.Y.Z` (or running it by hand with a tag)
tests, makes the Windows installer on a Windows runner and the Linux package on Linux, and publishes the
release with `Menhir-Setup-win-x64.exe` and `Menhir-Setup-linux-x64.zip`. The version of a build is the tag's
(Directory.Build.props has the one of the sources). The editor reads the releases of a public repository
without any token.

## Making the package

```sh
Installer/package.sh                     # win-x64: the Windows installer (needs Inno Setup 6's ISCC)
Installer/package.sh win-x64 --zip       # win-x64 with the console installer instead
Installer/package.sh linux-x64
Installer/package.sh linux-x64 --framework-dependent
```

On Windows run it from Git Bash; ISCC is found in Inno Setup's folder or on the PATH. From Linux it works with
Wine: `ISCC="wine C:/Inno/ISCC.exe" Installer/package.sh` (Inno Setup 6 installed in the Wine prefix, which
needs wine32). It writes `artifacts/Menhir-Setup-win-x64.exe`, or for zip packages
`artifacts/Menhir-Setup-<rid>/` (the installer and `Menhir.zip`) and a zip of that folder.

The installers are not signed: Windows SmartScreen shows "Windows protected your PC" (More info → Run anyway),
and Smart App Control, when it is on, may refuse them.
