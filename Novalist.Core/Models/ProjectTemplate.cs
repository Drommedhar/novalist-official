using System.Collections.Generic;

namespace Novalist.Core.Models;

public sealed class ProjectTemplate
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<TemplateChapter> Chapters { get; set; } = [];
    public IReadOnlyList<TemplateBeat> TimelineBeats { get; set; } = [];
}

public sealed class TemplateChapter
{
    public string Title { get; set; } = string.Empty;
    public string Act { get; set; } = string.Empty;
    public IReadOnlyList<TemplateScene> Scenes { get; set; } = [];
}

public sealed class TemplateScene
{
    public string Title { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
}

public sealed class TemplateBeat
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryId { get; set; } = "plot";
}
