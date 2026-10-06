using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    public async Task<int> SyncMentionDisplayTextAsync(string entityId, string newDisplayText)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return 0;
        if (string.IsNullOrEmpty(entityId)) return 0;

        var modified = 0;
        var escapedDisplay = System.Net.WebUtility.HtmlEncode(newDisplayText);

        // Active scenes
        foreach (var entry in ScenesManifest.Chapters)
        {
            var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == entry.Key);
            if (chapter == null) continue;

            foreach (var scene in entry.Value)
            {
                var path = GetSceneFilePath(chapter, scene);
                if (await TryRewriteSceneMentionsAsync(path, entityId, escapedDisplay))
                    modified++;
            }
        }

        // Archived scenes
        foreach (var scene in ScenesManifest.Archived)
        {
            var path = GetArchivedSceneFilePath(scene);
            if (await TryRewriteSceneMentionsAsync(path, entityId, escapedDisplay))
                modified++;
        }

        return modified;
    }

    private async Task<bool> TryRewriteSceneMentionsAsync(string path, string entityId, string newDisplayHtml)
    {
        if (!await _fileService.ExistsAsync(path)) return false;
        var content = await _fileService.ReadTextAsync(path);
        if (string.IsNullOrEmpty(content) || content.IndexOf("nv-entity-mention", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        var changed = false;
        var rewritten = MentionSpanRegex.Replace(content, match =>
        {
            var attrsLeft = match.Groups[1].Value;
            var attrsRight = match.Groups[2].Value;
            var inner = match.Groups[3].Value;
            var attrs = attrsLeft + attrsRight;

            var idMatch = Regex.Match(attrs, @"data-entity-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!idMatch.Success || !string.Equals(idMatch.Groups[1].Value, entityId, StringComparison.Ordinal))
                return match.Value;

            var sourceMatch = Regex.Match(attrs, @"data-mention-source\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (sourceMatch.Success && string.Equals(sourceMatch.Groups[1].Value, "manual", StringComparison.OrdinalIgnoreCase))
                return match.Value;
            if (sourceMatch.Success && string.Equals(sourceMatch.Groups[1].Value, "alias", StringComparison.OrdinalIgnoreCase))
                return match.Value; // alias-sourced spans keep their text

            if (string.Equals(inner, newDisplayHtml, StringComparison.Ordinal))
                return match.Value;

            changed = true;
            // Reconstruct, preserving original attributes verbatim.
            return $"<span {attrsLeft}class=\"nv-entity-mention\"{attrsRight}>{newDisplayHtml}</span>";
        });

        if (!changed) return false;
        await _fileService.WriteTextAsync(path, rewritten);
        return true;
    }

    public string GetChapterFolderPath(ChapterData chapter)
    {
        var root = ActiveDraftRoot ?? ActiveBookRoot;
        if (ActiveBook is not { } book || root == null)
            throw new InvalidOperationException("No book active.");
        return _fileService.CombinePath(root, book.ChapterFolder, chapter.FolderName);
    }

    public string GetSceneFilePath(ChapterData chapter, SceneData scene)
    {
        return _fileService.CombinePath(GetChapterFolderPath(chapter), scene.FileName);
    }

    public async Task<string> ReadSceneContentAsync(ChapterData chapter, SceneData scene)
    {
        var path = GetSceneFilePath(chapter, scene);
        if (await _fileService.ExistsAsync(path))
            return FileFrontMatter.Strip(await _fileService.ReadTextAsync(path));
        return string.Empty;
    }

    public async Task WriteSceneContentAsync(ChapterData chapter, SceneData scene, string content)
    {
        var path = GetSceneFilePath(chapter, scene);
        // Re-stamp the identity front-matter: strip any echoed-back marker, then prepend the
        // canonical one. Keeps the scene's id durable across every save without leaking the
        // comment into the editor's content.
        var stamped = FileFrontMatter.Stamp(FileFrontMatter.Strip(content), scene.Id);
        await _fileService.WriteTextAsync(path, stamped);
    }

    public List<ChapterData> GetChaptersOrdered()
    {
        return ActiveBook?.Chapters.OrderBy(c => c.Order).ToList() ?? new List<ChapterData>();
    }

    public List<SceneData> GetScenesForChapter(string chapterGuid)
    {
        if (ScenesManifest?.Chapters.TryGetValue(chapterGuid, out var scenes) == true)
            return scenes.OrderBy(s => s.Order).ToList();
        return new List<SceneData>();
    }
}
