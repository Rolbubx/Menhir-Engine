using System.Diagnostics;

namespace Menhir.Setup;

/// <summary>
/// Removes what the installer put in the installation folder (install.json lists it), the shortcuts, the entry in
/// Settings > Apps and DOTNET_ROOT if the installer set it. The user's projects are elsewhere and stay; the
/// editor's preferences (layout, recent projects) stay unless asked.
/// </summary>
internal static class Uninstaller
{
    public static int Run(string? folderOption)
    {
        Ui.ShowLogo($"{Installer.ProgramName} - Uninstall");

        string folder = folderOption ?? (OperatingSystem.IsWindows() ? WindowsIntegration.InstalledFolder() : null) ?? AppContext.BaseDirectory;
        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Manifest? manifest = Manifest.Load(folder);
        if (manifest == null)
        {
            Ui.Error($"No installation of Menhir in {folder} (install.json is missing).");
            return 2;
        }

        Ui.Info($"Menhir {manifest.Version} is installed in {folder}.");
        Ui.Dim("Your projects are not touched, wherever they are (except inside this folder's Samples/).");
        if (!Ui.Confirm("Uninstall Menhir?", true))
        {
            Ui.Info("Nothing was removed.");
            return 1;
        }
        bool samples = !manifest.Entries.Contains("Samples") || Ui.Confirm("Remove the sample project (Samples/) too?", true);
        string preferences = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Menhir")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Menhir");
        bool removePreferences = Directory.Exists(preferences) &&
                                 Ui.Confirm($"Remove the editor's preferences too (layout, recent projects in {preferences})?", false);

        Console.WriteLine();
        foreach (string shortcut in manifest.Shortcuts)
            Installer.Delete(shortcut);
        if (OperatingSystem.IsWindows())
        {
            WindowsIntegration.Unregister();
            if (manifest.DotnetRootSet != null && WindowsIntegration.GetUserVariable("DOTNET_ROOT") == manifest.DotnetRootSet)
                WindowsIntegration.SetUserVariable("DOTNET_ROOT", null);
        }
        if (removePreferences)
            Installer.Delete(preferences);

        string self = Path.GetFullPath(Environment.ProcessPath!);
        bool selfInside = false;
        foreach (string entry in manifest.Entries)
        {
            if (entry == "Samples" && !samples)
                continue;
            string path = Path.Combine(folder, entry);
            if (string.Equals(Path.GetFullPath(path), self, StringComparison.OrdinalIgnoreCase))
            {
                selfInside = true; // a running program cannot delete itself on Windows: done after it exits
                continue;
            }
            Installer.Delete(path);
        }

        if (selfInside && OperatingSystem.IsWindows())
        {
            // cmd waits for this program to end, then removes it and the folder if nothing else is left.
            string script = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\" & rmdir \"{folder}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", script) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
        }
        else
        {
            if (selfInside)
                Installer.Delete(self);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }

        Ui.Write("\n  Menhir is uninstalled.\n", ConsoleColor.Green);
        if (!samples || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(e => !string.Equals(e, self, StringComparison.OrdinalIgnoreCase)))
            Ui.Dim($"What you kept or added is still in {folder}.");
        return 0;
    }
}
