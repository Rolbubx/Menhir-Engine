namespace Menhir.Editor.Projects;

/// <summary>ProjectSettings/ProjectSettings.json: project-wide settings, committed with the project.</summary>
public sealed class ProjectSettings
{
    public const string FileName = "ProjectSettings.json";

    public int Version { get; set; } = 1;

    public string Name { get; set; } = "";
}
