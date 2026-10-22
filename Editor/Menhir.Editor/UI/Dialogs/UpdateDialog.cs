using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Logging;
using Menhir.Editor.Updates;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>
/// File > Update Editor: looks for a newer release on GitHub, shows what is new, and on "Update and Restart"
/// downloads it and hands over to its installer (see <see cref="EditorUpdate"/>), which restarts the editor
/// on the open project.
/// </summary>
public sealed class UpdateDialog : ModalDialog
{
    private readonly UpdateChecker _checker = new(UpdateChecker.CreateClient());
    private readonly Action<Action> _confirmDiscardChanges;
    private readonly Action<string> _runInstaller;

    private Task<Release?>? _check;
    private Task<string>? _download;
    private CancellationTokenSource? _cancel;
    private Release? _release;
    private string? _error;
    private double _progress;

    /// <param name="confirmDiscardChanges">Runs the action once unsaved scene changes are saved or discarded.</param>
    /// <param name="runInstaller">Starts the downloaded installer and closes the editor.</param>
    public UpdateDialog(Action<Action> confirmDiscardChanges, Action<string> runInstaller)
    {
        _confirmDiscardChanges = confirmDiscardChanges;
        _runInstaller = runInstaller;
    }

    protected override string Title => "Update Editor";

    /// <summary>A newer version found by <see cref="CheckInBackground"/>, or null.</summary>
    public Release? Available { get; private set; }

    public void Open()
    {
        StartCheck();
        RequestOpen();
    }

    /// <summary>At startup: looks quietly, and only logs when there is something new.</summary>
    public void CheckInBackground()
    {
        if (EditorUpdate.InstallationFolder() == null)
            return;
        EditorUpdate.CleanUp();
        Task.Run(async () =>
        {
            try
            {
                if (await _checker.LatestAsync() is { } release && release.Version > UpdateChecker.CurrentVersion)
                {
                    Available = release;
                    Log.Info($"Menhir {release.Version} is available (this is {UpdateChecker.CurrentVersion}): File > Update Editor.");
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Offline: say nothing until asked.
            }
        });
    }

    private void StartCheck()
    {
        _error = null;
        _release = null;
        _download = null;
        _check = _checker.LatestAsync();
    }

    protected override void DrawContent()
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34);
        ImGui.TextUnformatted($"This is Menhir {UpdateChecker.CurrentVersion}.");
        string? installation = EditorUpdate.InstallationFolder();

        if (_check is { IsCompleted: true } check)
        {
            _check = null;
            if (check.IsCompletedSuccessfully)
                _release = check.Result;
            else
                _error = $"GitHub cannot be reached: {check.Exception?.GetBaseException().Message}";
        }
        if (_download is { IsCompleted: true } download)
        {
            _download = null;
            if (download.IsCompletedSuccessfully)
            {
                Close();
                _runInstaller(download.Result);
            }
            else if (!download.IsCanceled)
                _error = $"The download failed: {download.Exception?.GetBaseException().Message}";
        }

        ImGui.Spacing();
        bool canUpdate = false;
        if (_error != null)
            ErrorText(_error);
        else if (_check != null)
            ImGui.TextDisabled("Looking for a newer version on GitHub...");
        else if (_release == null || _release.Version <= UpdateChecker.CurrentVersion)
            ImGui.TextColored(EditorTheme.Text, _release == null
                ? $"No release with {UpdateChecker.PackageName} was found on GitHub ({UpdateChecker.Repository})."
                : "You have the latest version.");
        else
        {
            ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.45f, 1), $"Menhir {_release.Version} is available ({_release.Name}).");
            if (_release.Notes.Length > 0)
            {
                ImGui.Spacing();
                ImGui.BeginChild("##notes", new Vector2(ImGui.GetFontSize() * 34, ImGui.GetTextLineHeightWithSpacing() * 8), ImGuiChildFlags.Border);
                ImGui.TextUnformatted(_release.Notes);
                ImGui.EndChild();
            }
            if (installation == null)
                ImGui.TextDisabled("This editor runs from the sources, not from an installation: update them with git pull.");
            else
            {
                ImGui.TextDisabled($"It replaces the installation in {installation}, then the editor restarts on this project.");
                canUpdate = _download == null;
            }
        }

        if (_download != null)
        {
            ImGui.Spacing();
            ImGui.ProgressBar((float)_progress, new Vector2(-1, 0), $"Downloading {_progress:P0}");
        }
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        if (Button("Update and Restart", canUpdate, primary: true))
        {
            // The question about unsaved changes is a dialog of its own: this one comes back for the progress.
            Close();
            _confirmDiscardChanges(StartDownload);
        }
        ImGui.SameLine();
        if (Button("Check Again", _check == null && _download == null))
            StartCheck();
        ImGui.SameLine();
        if (Button(_download != null ? "Cancel" : "Close") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _cancel?.Cancel();
            _download = null;
            Close();
        }
    }

    private void StartDownload()
    {
        if (_release is not { } release)
            return;
        RequestOpen();
        _cancel = new CancellationTokenSource();
        _progress = 0;
        _download = EditorUpdate.PrepareAsync(_checker, release, new Progress<double>(p => _progress = p), _cancel.Token);
    }
}
