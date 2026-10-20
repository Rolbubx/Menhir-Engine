namespace Menhir.Editor.Projects;

/// <param name="Id">Saved in ProjectSettings.json as the project type.</param>
public sealed record ProjectTemplate(string Id, string DisplayName, string Description);

/// <summary>The kinds of project the New Project page offers.</summary>
public static class ProjectTemplates
{
    public static readonly ProjectTemplate UniversalPipeline = new(
        "MenhirUniversalPipeline",
        "Menhir Universal Pipeline",
        "The default renderer, for 3D games on every platform.");

    public static IReadOnlyList<ProjectTemplate> All { get; } = [UniversalPipeline];

    public static ProjectTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
