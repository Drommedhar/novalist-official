using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IProjectTemplateService
{
    IReadOnlyList<ProjectTemplate> GetTemplates();
    ProjectTemplate? GetById(string id);
    Task ApplyAsync(IProjectService projectService, ProjectTemplate template);
}
