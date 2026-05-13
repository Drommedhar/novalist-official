using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Sdk.Models.Wizards;
using Novalist.Core.Services;

namespace Novalist.Desktop.Services.Wizards;

/// <summary>
/// Maps the Project Snowflake wizard's <see cref="WizardResult"/> onto a real
/// project: creates the project, seeds acts + chapters under each act, and
/// adds protagonist / antagonist characters.
/// </summary>
public static class ProjectWizardMapper
{
    public static string ExtractProjectName(WizardResult result)
        => result.GetText("projectName");

    public static string ExtractBookName(WizardResult result)
    {
        var b = result.GetText("bookName");
        if (!string.IsNullOrWhiteSpace(b)) return b;
        var p = ExtractProjectName(result);
        return string.IsNullOrWhiteSpace(p) ? "Book 1" : p;
    }

    public static async Task ApplyAsync(IProjectService projectService, IEntityService entityService, WizardResult result)
    {
        if (projectService.ActiveBook == null) return;

        // Seed acts + chapters.
        var chaptersPerAct = Math.Max(1, result.GetNumber("chaptersPerAct", 7));
        var actTitles = new[]
        {
            ("act1", "Act 1 — Setup"),
            ("act2", "Act 2 — Confrontation"),
            ("act3", "Act 3 — Resolution"),
        };

        foreach (var (key, title) in actTitles)
        {
            var act = new ActData { Name = title };
            projectService.ActiveBook.Acts.Add(act);
            for (int i = 1; i <= chaptersPerAct; i++)
            {
                var chapter = await projectService.CreateChapterAsync($"Chapter {i}");
                chapter.Act = title;
            }
        }
        await projectService.SaveProjectAsync();

        // Seed protagonists + antagonists.
        await SeedCharactersAsync(entityService, result, "protagonists", role: "Protagonist");
        await SeedCharactersAsync(entityService, result, "antagonists", role: "Antagonist");

        // Stash premise + paragraph as the first scene's synopsis (best-effort).
        var firstChapter = projectService.ActiveBook.Chapters.FirstOrDefault();
        if (firstChapter != null)
        {
            var firstScene = projectService.GetScenesForChapter(firstChapter.Guid).FirstOrDefault();
            if (firstScene == null)
            {
                firstScene = await projectService.CreateSceneAsync(firstChapter.Guid, "Scene 1");
            }
            firstScene.Synopsis = BuildOverviewNotes(result);
            await projectService.SaveScenesAsync();
        }
    }

    private static async Task SeedCharactersAsync(IEntityService entityService, WizardResult result, string stepId, string role)
    {
        var list = result.GetList(stepId);
        foreach (var entry in list)
        {
            var name = entry.TryGetValue("name", out var n) ? n.Text ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var entityRole = entry.TryGetValue("role", out var r) && !string.IsNullOrWhiteSpace(r.Text) ? r.Text! : role;
            var character = new CharacterData
            {
                Name = name.Trim(),
                Role = entityRole,
            };
            await entityService.SaveCharacterAsync(character);
        }
    }

    private static string BuildOverviewNotes(WizardResult result)
    {
        var sb = new System.Text.StringBuilder();
        var premise = result.GetText("premise");
        if (!string.IsNullOrWhiteSpace(premise)) sb.AppendLine("Premise: " + premise);
        var paragraph = result.GetText("paragraph");
        if (!string.IsNullOrWhiteSpace(paragraph)) { sb.AppendLine(); sb.AppendLine(paragraph); }
        var a1 = result.GetText("actOne");
        var a2 = result.GetText("actTwo");
        var a3 = result.GetText("actThree");
        if (!string.IsNullOrWhiteSpace(a1)) { sb.AppendLine(); sb.AppendLine("Act 1: " + a1); }
        if (!string.IsNullOrWhiteSpace(a2)) { sb.AppendLine(); sb.AppendLine("Act 2: " + a2); }
        if (!string.IsNullOrWhiteSpace(a3)) { sb.AppendLine(); sb.AppendLine("Act 3: " + a3); }
        return sb.ToString();
    }
}
