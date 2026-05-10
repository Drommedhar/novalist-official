using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class ProjectTemplateService : IProjectTemplateService
{
    private readonly List<ProjectTemplate> _templates;

    public ProjectTemplateService()
    {
        _templates = BuildBuiltInTemplates();
    }

    public IReadOnlyList<ProjectTemplate> GetTemplates() => _templates;

    public ProjectTemplate? GetById(string id)
        => _templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task ApplyAsync(IProjectService projectService, ProjectTemplate template)
    {
        if (string.Equals(template.Id, "blank", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var tc in template.Chapters)
        {
            var chapter = await projectService.CreateChapterAsync(tc.Title);
            if (!string.IsNullOrWhiteSpace(tc.Act))
            {
                chapter.Act = tc.Act;
            }

            foreach (var ts in tc.Scenes)
            {
                var scene = await projectService.CreateSceneAsync(chapter.Guid, ts.Title);
                if (!string.IsNullOrWhiteSpace(ts.Synopsis))
                    scene.Synopsis = ts.Synopsis;
            }
        }

        await projectService.SaveScenesAsync();
    }

    private static List<ProjectTemplate> BuildBuiltInTemplates()
    {
        return
        [
            new ProjectTemplate
            {
                Id = "blank",
                DisplayName = "Blank",
                Description = "Start with an empty project."
            },
            new ProjectTemplate
            {
                Id = "three-act",
                DisplayName = "Three-Act Novel",
                Description = "Classic three-act structure with setup, confrontation, and resolution.",
                Chapters =
                [
                    new TemplateChapter { Title = "Setup", Act = "I", Scenes = [
                        new TemplateScene { Title = "Opening Image", Synopsis = "Establish the world and protagonist's status quo." },
                        new TemplateScene { Title = "Inciting Incident", Synopsis = "Disrupt the status quo." }
                    ]},
                    new TemplateChapter { Title = "Confrontation", Act = "II", Scenes = [
                        new TemplateScene { Title = "Rising Action", Synopsis = "Protagonist faces escalating obstacles." },
                        new TemplateScene { Title = "Midpoint", Synopsis = "Major shift — false victory or false defeat." },
                        new TemplateScene { Title = "Crisis", Synopsis = "All seems lost." }
                    ]},
                    new TemplateChapter { Title = "Resolution", Act = "III", Scenes = [
                        new TemplateScene { Title = "Climax", Synopsis = "Final confrontation." },
                        new TemplateScene { Title = "Denouement", Synopsis = "New status quo." }
                    ]}
                ]
            },
            new ProjectTemplate
            {
                Id = "save-the-cat",
                DisplayName = "Save the Cat (15 beats)",
                Description = "Blake Snyder's beat sheet structure.",
                Chapters =
                [
                    new TemplateChapter { Title = "Act 1", Act = "I", Scenes = [
                        new TemplateScene { Title = "Opening Image", Synopsis = "Snapshot of the protagonist's life before transformation." },
                        new TemplateScene { Title = "Theme Stated", Synopsis = "What the story is really about." },
                        new TemplateScene { Title = "Set-Up", Synopsis = "Introduce world, characters, stakes." },
                        new TemplateScene { Title = "Catalyst", Synopsis = "Life-changing event." },
                        new TemplateScene { Title = "Debate", Synopsis = "Should I go?" },
                        new TemplateScene { Title = "Break Into Two", Synopsis = "Protagonist commits." }
                    ]},
                    new TemplateChapter { Title = "Act 2A", Act = "II", Scenes = [
                        new TemplateScene { Title = "B Story", Synopsis = "Subplot / love interest." },
                        new TemplateScene { Title = "Fun and Games", Synopsis = "Promise of the premise." },
                        new TemplateScene { Title = "Midpoint", Synopsis = "False victory or false defeat." }
                    ]},
                    new TemplateChapter { Title = "Act 2B", Act = "II", Scenes = [
                        new TemplateScene { Title = "Bad Guys Close In", Synopsis = "Antagonist regroups; internal cracks show." },
                        new TemplateScene { Title = "All Is Lost", Synopsis = "Lowest point." },
                        new TemplateScene { Title = "Dark Night of the Soul", Synopsis = "Despair." },
                        new TemplateScene { Title = "Break Into Three", Synopsis = "Solution discovered." }
                    ]},
                    new TemplateChapter { Title = "Act 3", Act = "III", Scenes = [
                        new TemplateScene { Title = "Finale", Synopsis = "Execute the plan." },
                        new TemplateScene { Title = "Final Image", Synopsis = "Mirror of opening, showing change." }
                    ]}
                ]
            },
            new ProjectTemplate
            {
                Id = "hero-journey",
                DisplayName = "Hero's Journey",
                Description = "Campbell-style 12-stage monomyth.",
                Chapters =
                [
                    new TemplateChapter { Title = "Departure", Act = "I", Scenes = [
                        new TemplateScene { Title = "Ordinary World", Synopsis = "" },
                        new TemplateScene { Title = "Call to Adventure", Synopsis = "" },
                        new TemplateScene { Title = "Refusal of the Call", Synopsis = "" },
                        new TemplateScene { Title = "Meeting the Mentor", Synopsis = "" },
                        new TemplateScene { Title = "Crossing the Threshold", Synopsis = "" }
                    ]},
                    new TemplateChapter { Title = "Initiation", Act = "II", Scenes = [
                        new TemplateScene { Title = "Tests, Allies, Enemies", Synopsis = "" },
                        new TemplateScene { Title = "Approach to the Inmost Cave", Synopsis = "" },
                        new TemplateScene { Title = "Ordeal", Synopsis = "" },
                        new TemplateScene { Title = "Reward", Synopsis = "" }
                    ]},
                    new TemplateChapter { Title = "Return", Act = "III", Scenes = [
                        new TemplateScene { Title = "The Road Back", Synopsis = "" },
                        new TemplateScene { Title = "Resurrection", Synopsis = "" },
                        new TemplateScene { Title = "Return with the Elixir", Synopsis = "" }
                    ]}
                ]
            },
            new ProjectTemplate
            {
                Id = "non-fiction",
                DisplayName = "Non-Fiction",
                Description = "Introduction, parts, conclusion.",
                Chapters =
                [
                    new TemplateChapter { Title = "Introduction", Scenes = [
                        new TemplateScene { Title = "Hook", Synopsis = "Why this matters." },
                        new TemplateScene { Title = "Roadmap", Synopsis = "What the reader will learn." }
                    ]},
                    new TemplateChapter { Title = "Part 1", Scenes = [
                        new TemplateScene { Title = "Chapter 1", Synopsis = "" }
                    ]},
                    new TemplateChapter { Title = "Conclusion", Scenes = [
                        new TemplateScene { Title = "Recap", Synopsis = "" },
                        new TemplateScene { Title = "Call to Action", Synopsis = "" }
                    ]}
                ]
            }
        ];
    }
}
