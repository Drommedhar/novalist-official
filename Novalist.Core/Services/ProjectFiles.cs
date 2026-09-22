namespace Novalist.Core.Services;

/// <summary>Services created from a project inherit its platform file access.</summary>
internal static class ProjectFiles
{
    public static IFileService For(IProjectService project) =>
        (project as ProjectService)?.FileService ?? new FileService();
}
