using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Menhir.Setup;

/// <summary>
/// What makes Menhir an installed program on Windows, for the current user (no administrator rights):
/// its entry in Settings > Apps (the Uninstall key), shortcuts, and DOTNET_ROOT when the editor needs it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsIntegration
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MenhirEngine";

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Menhir Editor.lnk");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Menhir Editor.lnk");

    /// <summary>The folder of an existing installation, or null.</summary>
    public static string? InstalledFolder()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") as string;
    }

    public static void Register(string folder, string version, string editor, string uninstaller, long sizeKb)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Menhir Engine");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Menhir");
        key.SetValue("InstallLocation", folder);
        key.SetValue("DisplayIcon", editor);
        key.SetValue("UninstallString", $"\"{uninstaller}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --yes");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

    /// <summary>A .lnk shortcut, made by Windows' own WScript.Shell (through PowerShell).</summary>
    public static bool CreateShortcut(string shortcut, string target, string workingDirectory)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        string script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({Quote(shortcut)}); " +
                        $"$s.TargetPath = {Quote(target)}; $s.WorkingDirectory = {Quote(workingDirectory)}; " +
                        $"$s.IconLocation = {Quote(target + ",0")}; $s.Description = 'Menhir Engine editor'; $s.Save()";
        (int exit, _) = Compilers.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], null, 30_000);
        return exit == 0 && File.Exists(shortcut);
    }

    /// <summary>A variable of the user's environment, seen by programs started from then on.</summary>
    public static void SetUserVariable(string name, string? value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey("Environment");
        if (value == null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value);
        // Tell Explorer, so programs started from the Start menu get it without logging out.
        SendMessageTimeoutW(0xFFFF, 0x001A, 0, "Environment", 0x0002, 5000, out _);
    }

    public static string? GetUserVariable(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue(name) as string;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nint wParam, string lParam, uint flags,
        uint timeout, out nint result);
}
