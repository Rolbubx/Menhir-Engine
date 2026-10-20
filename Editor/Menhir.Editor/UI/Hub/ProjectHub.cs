using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;
using Menhir.Editor.UI.Dialogs;

namespace Menhir.Editor.UI.Hub;

/// <summary>
/// Where projects are opened and created, like Unity Hub. It fills the window when the
/// editor starts without a project, and opens as a large popup from File > New / Open Project.
/// </summary>
public sealed class ProjectHub
{
    public enum Page
    {
        Projects,
        NewProject,
    }

    private const string ModalId = "Menhir Hub";
    private const float LabelWidth = 150f;

    private readonly EditorPreferences _preferences;
    private readonly Action<Project> _openProject;
    private readonly string? _samplePath;
    private readonly FolderPickerPopup _folderPicker = new();

    private Page _page = Page.Projects;
    private bool _openModalRequested;
    private bool _closeModalRequested;
    private string? _error;

    // Projects page
    private string _openDirectory = "";
    private string _openRemote = "";

    // New Project page
    private int _templateIndex;
    private string _newName = "My Project";
    private string _newDirectory;
    private string _newRemote = "";
    private OwnerLeavePolicy _ownerLeavePolicy = OwnerLeavePolicy.TerminateAndSave;
    private AssetDelivery _assetDelivery = AssetDelivery.EncryptedCopy;

    public ProjectHub(EditorPreferences preferences, Action<Project> openProject, string? samplePath)
    {
        _preferences = preferences;
        _openProject = openProject;
        _samplePath = samplePath;
        _newDirectory = DefaultProjectsDirectory();
    }

    /// <summary>Opens the hub as a popup on <paramref name="page"/> (when a project is already open).</summary>
    public void Show(Page page)
    {
        _page = page;
        _error = null;
        _openModalRequested = true;
    }

    /// <summary>The hub as the whole window, when no project is open.</summary>
    public void DrawFullscreen()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking |
                                       ImGuiWindowFlags.NoBringToFrontOnFocus;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool visible = ImGui.Begin("##hub", flags);
        ImGui.PopStyleVar(3);
        if (visible)
            DrawBody();
        ImGui.End();
        _closeModalRequested = false;
    }

    /// <summary>The hub as a popup; call every frame (it only shows after <see cref="Show"/>).</summary>
    public void DrawModal()
    {
        if (_openModalRequested)
        {
            ImGui.OpenPopup(ModalId);
            _openModalRequested = false;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(Vector2.Min(new Vector2(980, 620), viewport.WorkSize * 0.92f), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool open = true;
        bool visible = ImGui.BeginPopupModal(ModalId, ref open, ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleVar();
        if (!visible)
            return;

        DrawBody();
        if (_closeModalRequested || ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
            ImGui.CloseCurrentPopup();
        _closeModalRequested = false;
        ImGui.EndPopup();
    }

    private void DrawBody()
    {
        DrawSidebar();
        ImGui.SameLine(0, 0);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28, 22));
        bool visible = ImGui.BeginChild("##page", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();
        if (visible)
        {
            if (_page == Page.Projects)
                DrawProjectsPage();
            else
                DrawNewProjectPage();
            // In this child, so it shares the ID stack of the Browse buttons that open it.
            _folderPicker.Draw();
        }
        ImGui.EndChild();
    }

    private void DrawSidebar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorTheme.TabBar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 20));
        bool visible = ImGui.BeginChild("##sidebar", new Vector2(220, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        if (visible)
        {
            ImGui.PushFont(EditorFonts.Bold);
            ImGui.TextUnformatted("Menhir");
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextDisabled("Hub");
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f));
            Vector2 size = new(0, ImGui.GetFrameHeight() + 6);
            if (ImGui.Selectable($"  {Icons.Project}   Projects", _page == Page.Projects, ImGuiSelectableFlags.None, size))
                SetPage(Page.Projects);
            if (ImGui.Selectable($"  {Icons.Plus}   New Project", _page == Page.NewProject, ImGuiSelectableFlags.None, size))
                SetPage(Page.NewProject);
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();
    }

    private void SetPage(Page page)
    {
        _page = page;
        _error = null;
    }

    private static void PageTitle(string title, string subtitle)
    {
        ImGui.PushFont(EditorFonts.Bold);
        ImGui.TextUnformatted(title);
        ImGui.PopFont();
        ImGui.TextDisabled(subtitle);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static void SectionTitle(string title)
    {
        ImGui.Spacing();
        ImGui.PushFont(EditorFonts.Bold);
        ImGui.SeparatorText(title);
        ImGui.PopFont();
    }

    // ---- Projects ----------------------------------------------------------------------------

    private void DrawProjectsPage()
    {
        PageTitle("Projects", "Open a recent project, or one from a folder or a remote.");

        SectionTitle("Recent");
        DrawRecentProjects();

        SectionTitle("Open");
        if (BeginForm("##openForm"))
        {
            FormRow("Project's dir");
            bool valid = Project.IsProjectFolder(_openDirectory.Trim());
            if (PathField("##openDir", ref _openDirectory, "Folder that contains Assets/", p => _openDirectory = p, "Open", valid))
                TryOpen(_openDirectory.Trim());
            if (_openDirectory.Trim().Length > 0 && !valid)
                ImGui.TextColored(EditorTheme.Warning, "This folder is not a Menhir project (no Assets folder).");

            FormRow("or Project's remote");
            ImGui.SetNextItemWidth(-ButtonWidth("Open") - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##openRemote", "http://", ref _openRemote, 512);
            ImGui.SameLine();
            ImGui.BeginDisabled();
            ImGui.Button("Open##remote", new Vector2(ButtonWidth("Open"), 0));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Opening a project from a remote is not available yet.");
            ImGui.TextDisabled($"{Icons.Info}  Remote projects are not available yet.");
            EndForm();
        }
        ErrorLine();
    }

    private void DrawRecentProjects()
    {
        List<string> paths = _preferences.RecentProjects.ToList();
        if (_samplePath != null && !paths.Any(p => SamePath(p, _samplePath)))
            paths.Add(_samplePath);
        if (paths.Count == 0)
        {
            ImGui.TextDisabled("No recent projects. Create one, or open a project folder below.");
            return;
        }

        float rowHeight = ImGui.GetTextLineHeight() * 2 + 14;
        float maxHeight = MathF.Max(rowHeight * 2, ImGui.GetContentRegionAvail().Y - 230);
        float height = MathF.Min(rowHeight * paths.Count + 4, maxHeight);
        if (!ImGui.BeginChild("##recent", new Vector2(0, height)))
        {
            ImGui.EndChild();
            return;
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        string? remove = null;
        foreach (string path in paths)
        {
            bool exists = Project.IsProjectFolder(path);
            ProjectSettings? settings = exists ? Project.ReadSettings(path) : null;
            string name = settings is { Name.Length: > 0 } ? settings.Name : Path.GetFileName(path);
            bool isSample = _samplePath != null && SamePath(path, _samplePath);

            ImGui.PushID(path);
            Vector2 min = ImGui.GetCursorScreenPos();
            ImGui.BeginDisabled(!exists);
            if (ImGui.Selectable("##project", false, ImGuiSelectableFlags.None, new Vector2(0, rowHeight - 4)))
                TryOpen(path);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(exists ? "Open" : "This folder is missing or no longer a project.");
            if (!isSample && ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem($"{Icons.Close}  Remove from List"))
                    remove = path;
                ImGui.EndPopup();
            }

            // Icon, name and type on the first line, path below.
            float x = min.X + 10;
            float y = min.Y + 5;
            uint dim = ImGui.GetColorU32(ImGuiCol.TextDisabled);
            drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(exists ? AssetIcons.Color(AssetImporters.Folder) : EditorTheme.TextDim), Icons.Folder);
            x += ImGui.CalcTextSize(Icons.Folder).X + 10;
            ImGui.PushFont(EditorFonts.Bold);
            drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(exists ? ImGuiCol.Text : ImGuiCol.TextDisabled), name);
            float nameWidth = ImGui.CalcTextSize(name).X;
            ImGui.PopFont();
            string tag = !exists ? "Missing" : isSample ? "Sample" : settings?.Remote != null ? "Remote" : "";
            if (tag.Length > 0)
                drawList.AddText(new Vector2(x + nameWidth + 10, y), ImGui.GetColorU32(exists ? EditorTheme.AccentBright : EditorTheme.Warning), tag);
            string type = ProjectTemplates.Find(settings?.Type ?? "")?.DisplayName ?? settings?.Type ?? "";
            float typeWidth = ImGui.CalcTextSize(type).X;
            drawList.AddText(new Vector2(min.X + ImGui.GetContentRegionAvail().X - typeWidth - 10, y), dim, type);
            drawList.AddText(new Vector2(x, y + ImGui.GetTextLineHeight() + 2), dim, path);
            ImGui.PopID();
        }
        ImGui.EndChild();

        if (remove != null)
        {
            _preferences.RecentProjects.RemoveAll(p => SamePath(p, remove));
            _preferences.Save(EditorPaths.PreferencesFile);
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            Project project = Project.Open(path);
            _error = null;
            _closeModalRequested = true;
            _openProject(project);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = e.Message;
            Log.Error($"Could not open project '{path}': {e.Message}");
        }
    }

    // ---- New Project -------------------------------------------------------------------------

    private void DrawNewProjectPage()
    {
        PageTitle("New Project", "Pick a type, a name and where to create it.");

        string name = _newName.Trim();
        string remote = _newRemote.Trim();
        string? remoteError = null;
        bool remoteValid = remote.Length == 0 || ProjectRemoteSettings.IsValidUrl(remote, out remoteError);

        if (BeginForm("##newForm"))
        {
            FormRow("Project type");
            IReadOnlyList<ProjectTemplate> templates = ProjectTemplates.All;
            ImGui.SetNextItemWidth(-float.Epsilon);
            if (ImGui.BeginCombo("##type", templates[_templateIndex].DisplayName))
            {
                for (int i = 0; i < templates.Count; i++)
                {
                    if (ImGui.Selectable(templates[i].DisplayName, i == _templateIndex))
                        _templateIndex = i;
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(templates[_templateIndex].Description);

            FormRow("Project name");
            ImGui.SetNextItemWidth(-float.Epsilon);
            ImGui.InputText("##name", ref _newName, 128);

            FormRow("Project dir");
            PathField("##dir", ref _newDirectory, "Folder to create the project in", p => _newDirectory = p, null, false);
            ImGui.TextDisabled(name.Length > 0 ? $"Creates {Path.Combine(_newDirectory.Trim(), name)}" : "Enter a name.");

            FormRow("Project remote");
            ImGui.SetNextItemWidth(-float.Epsilon);
            ImGui.InputTextWithHint("##remote", "http://   (optional)", ref _newRemote, 512);
            if (remoteError != null)
                ImGui.TextColored(EditorTheme.Error, remoteError);
            else
                ImGui.TextDisabled("Optional. Lets collaborators with your token work on the project with you.");
            EndForm();
        }

        if (remote.Length > 0)
            DrawRemoteOptions();

        ImGui.Spacing();
        ImGui.Spacing();
        ErrorLine();
        float width = ButtonWidth("Create Project");
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - width);
        bool canCreate = name.Length > 0 && _newDirectory.Trim().Length > 0 && remoteValid;
        ImGui.BeginDisabled(!canCreate);
        bool create = EditorTheme.PrimaryButton("Create Project", new Vector2(width, ImGui.GetFrameHeight() + 6));
        ImGui.EndDisabled();
        if (create && canCreate)
            Create(name, remote);
    }

    private void DrawRemoteOptions()
    {
        SectionTitle("Remote");

        ImGui.TextUnformatted("If you quit while others are working:");
        ImGui.Indent();
        Choice(ref _ownerLeavePolicy, OwnerLeavePolicy.TerminateWithoutSaving,
            "Terminate their sessions (without saving)", "Not recommended", EditorTheme.Warning, null);
        Choice(ref _ownerLeavePolicy, OwnerLeavePolicy.TerminateAndSave,
            "Terminate their sessions and save their work", null, default,
            "The remote keeps their work in a GitHub repository, with a private key.");
        Choice(ref _ownerLeavePolicy, OwnerLeavePolicy.HandOverToCollaborator,
            "Let them continue", "Not recommended", EditorTheme.Warning,
            "One of the collaborators becomes the second admin. When you reconnect while they are connected, you get their version.");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextUnformatted("Files sent to collaborators:");
        ImGui.Indent();
        Choice(ref _assetDelivery, AssetDelivery.StreamedChunks,
            "Streamed chunks only", "Not recommended: security level 1", EditorTheme.Warning,
            "Collaborators never get whole files; chunks are streamed as needed.");
        Choice(ref _assetDelivery, AssetDelivery.EncryptedCopy,
            "Encrypted copy with a secret key", null, default,
            "Collaborators get a copy of the game they can decrypt. Files uploaded later need a newer key.");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextColored(EditorTheme.AccentBright, $"{Icons.Info}  Remote collaboration is not implemented yet: " +
                                                     "these options are saved with the project for when it is.");
    }

    /// <summary>A radio button with an optional colored tag after it and a dimmed explanation below.</summary>
    private static void Choice<T>(ref T current, T value, string label, string? tag, Vector4 tagColor, string? description)
        where T : struct, Enum
    {
        if (ImGui.RadioButton($"{label}##{value}", current.Equals(value)))
            current = value;
        if (tag != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(tagColor, $"({tag})");
        }
        if (description != null)
        {
            ImGui.Indent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.TextDim);
            ImGui.TextWrapped(description);
            ImGui.PopStyleColor();
            ImGui.Unindent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
        }
    }

    private void Create(string name, string remote)
    {
        try
        {
            ProjectRemoteSettings? remoteSettings = remote.Length == 0 ? null : new ProjectRemoteSettings
            {
                Url = remote,
                OnOwnerLeave = _ownerLeavePolicy,
                AssetDelivery = _assetDelivery,
            };
            Project project = Project.Create(_newDirectory.Trim(), name, ProjectTemplates.All[_templateIndex], remoteSettings);
            Log.Info($"Created project {project.RootPath}");
            _error = null;
            _closeModalRequested = true;
            _newName = "My Project";
            _newRemote = "";
            _openProject(project);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = e.Message;
        }
    }

    // ---- Form helpers ------------------------------------------------------------------------

    private static bool BeginForm(string id)
    {
        if (!ImGui.BeginTable(id, 2))
            return false;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, LabelWidth);
        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch);
        return true;
    }

    private static void EndForm() => ImGui.EndTable();

    private static void FormRow(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label.ToUpperInvariant());
        ImGui.TableSetColumnIndex(1);
    }

    /// <summary>
    /// A path field with a Browse button (the picked folder goes to <paramref name="onBrowsed"/>), and an action button when <paramref name="action"/> is set.
    /// Returns true when the action button is clicked (or Enter is pressed in the field) while <paramref name="actionEnabled"/>.
    /// </summary>
    private bool PathField(string id, ref string path, string hint, Action<string> onBrowsed, string? action, bool actionEnabled)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float buttons = ButtonWidth("Browse...") + (action != null ? ButtonWidth(action) + style.ItemSpacing.X : 0);
        ImGui.SetNextItemWidth(-buttons - style.ItemSpacing.X);
        bool submitted = ImGui.InputTextWithHint(id, hint, ref path, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button($"Browse...{id}", new Vector2(ButtonWidth("Browse..."), 0)))
        {
            string start = Directory.Exists(path.Trim()) ? path.Trim() : DefaultProjectsDirectory();
            _folderPicker.Open(start, onBrowsed);
        }
        if (action == null)
            return false;

        ImGui.SameLine();
        ImGui.BeginDisabled(!actionEnabled);
        bool clicked = EditorTheme.PrimaryButton($"{action}{id}", new Vector2(ButtonWidth(action), 0));
        ImGui.EndDisabled();
        return actionEnabled && (clicked || submitted);
    }

    private static float ButtonWidth(string label) =>
        MathF.Max(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2, ImGui.GetFontSize() * 5.5f);

    private void ErrorLine()
    {
        if (_error != null)
            ImGui.TextColored(EditorTheme.Error, $"{Icons.Error}  {_error}");
    }

    /// <summary>The folder of the most recent project, or the user's home folder.</summary>
    private string DefaultProjectsDirectory()
    {
        string? recent = _preferences.RecentProjects.FirstOrDefault(Directory.Exists);
        if (recent != null && Directory.GetParent(recent) is { } parent)
            return parent.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
