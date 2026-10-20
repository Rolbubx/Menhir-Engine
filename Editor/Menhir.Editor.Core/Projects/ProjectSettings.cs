namespace Menhir.Editor.Projects;

/// <summary>ProjectSettings/ProjectSettings.json: project-wide settings, committed with the project.</summary>
public sealed class ProjectSettings
{
    public const string FileName = "ProjectSettings.json";

    public int Version { get; set; } = 1;

    public string Name { get; set; } = "";

    /// <summary>Id of the <see cref="ProjectTemplate"/> the project was created from.</summary>
    public string Type { get; set; } = ProjectTemplates.UniversalPipeline.Id;

    /// <summary>Set when the project is shared through a remote.</summary>
    public ProjectRemoteSettings? Remote { get; set; }

    public RenderingSettings Rendering { get; set; } = new();

    public BuildSettings Build { get; set; } = new();
}
