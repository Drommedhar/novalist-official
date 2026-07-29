using System.Xml.Linq;

namespace Novalist.Core.Services;

/// <summary>One item of a Scrivener binder that carried text, flattened into
/// the chapter and scene Novalist will create for it.</summary>
public sealed record ScrivenerScene(
    string ChapterTitle,
    string Title,
    string Text,
    string Synopsis);

/// <summary>What a Scrivener project turned into, plus what was left behind.</summary>
public sealed class ScrivenerProject
{
    public IReadOnlyList<ScrivenerScene> Scenes { get; init; } = [];

    /// <summary>"2" or "3" - which Scrivener laid the project out. Empty when
    /// the folder could not be read at all.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// What the import will not bring across, in the writer's terms.
    ///
    /// Scrivener holds a great deal Novalist has no equivalent for, and a silent
    /// import that drops half a research folder is worse than one that says so
    /// before it starts.
    /// </summary>
    public IReadOnlyList<string> Losses { get; init; } = [];

    public bool IsEmpty => Scenes.Count == 0;
}

/// <summary>
/// Reads a Scrivener project folder.
///
/// Both layouts are handled because both are in the wild: Scrivener 2 numbers
/// its documents and keeps them in <c>Files/Docs/&lt;id&gt;.rtf</c>, Scrivener 3
/// gives each a UUID folder under <c>Files/Data/</c>. The binder tree in the
/// <c>.scrivx</c> is the same shape in both, which is what makes one reader
/// enough.
///
/// The import is deliberately lossy and says so. Novalist has no equivalent for
/// a label, a collection, or a snapshot history, and pretending otherwise would
/// leave a writer discovering the gaps one at a time.
/// </summary>
public static class ScrivenerReader
{
    /// <summary>The extension a Scrivener project folder carries.</summary>
    public const string ProjectExtension = ".scriv";

    /// <summary>Binder folders Scrivener creates itself, which are not the
    /// writer's manuscript. Their contents are reported as a loss rather than
    /// imported as chapters.</summary>
    private static readonly string[] NonManuscriptFolders =
        ["Research", "Trash", "Templates", "Template Sheets", "Front Matter"];

    /// <summary>Whether a path looks like a Scrivener project. True for the
    /// folder and for the .scrivx inside it, since a file picker gives one or
    /// the other depending on the platform.</summary>
    public static bool LooksLikeScrivener(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path))
            return Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);
        return Path.GetExtension(path).Equals(".scrivx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a project. Returns an empty result rather than throwing for
    /// anything unreadable: an import that cannot start should say so in the
    /// dialog, not crash the app.
    /// </summary>
    public static ScrivenerProject Read(string path)
    {
        try
        {
            var root = ResolveRoot(path);
            if (root == null) return new ScrivenerProject();

            var scrivx = Directory.EnumerateFiles(root, "*.scrivx").FirstOrDefault();
            if (scrivx == null) return new ScrivenerProject();

            var binder = XDocument.Load(scrivx).Descendants("Binder").FirstOrDefault();
            if (binder == null) return new ScrivenerProject();

            // Scrivener 3 keeps documents under Files/Data; Scrivener 2 under
            // Files/Docs. Which folder exists is what tells them apart.
            var version = Directory.Exists(Path.Combine(root, "Files", "Data")) ? "3" : "2";

            var scenes = new List<ScrivenerScene>();
            var losses = new List<string>();
            foreach (var item in binder.Elements("BinderItem"))
                Walk(item, root, version, chapterTitle: string.Empty, scenes, losses);

            return new ScrivenerProject
            {
                Scenes = scenes,
                Version = version,
                Losses = [.. losses.Distinct(StringComparer.Ordinal)]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            return new ScrivenerProject();
        }
    }

    /// <summary>The project folder, whether the caller pointed at it or at the
    /// .scrivx inside it.</summary>
    private static string? ResolveRoot(string path)
    {
        if (Directory.Exists(path)) return path;
        if (File.Exists(path)) return Path.GetDirectoryName(path);
        return null;
    }

    /// <summary>
    /// Walks the binder, turning folders into chapters and text documents into
    /// their scenes.
    ///
    /// A binder is arbitrarily deep and Novalist is two levels, so everything
    /// below the first folder is flattened into it. That loses nesting rather
    /// than losing text, which is the right way round.
    /// </summary>
    private static void Walk(
        XElement item, string root, string version,
        string chapterTitle, List<ScrivenerScene> scenes, List<string> losses)
    {
        var title = (string?)item.Element("Title") ?? string.Empty;
        var type = (string?)item.Attribute("Type") ?? string.Empty;

        if (NonManuscriptFolders.Contains(title, StringComparer.OrdinalIgnoreCase))
        {
            losses.Add(title);
            return;
        }

        var children = item.Element("Children");
        var isFolder = type.Equals("Folder", StringComparison.OrdinalIgnoreCase)
                       || (children != null && children.Elements("BinderItem").Any());

        if (isFolder)
        {
            // The outermost folder names the chapter; deeper ones keep it, so a
            // three-level binder still lands as chapter and scene.
            var nextChapter = chapterTitle.Length > 0 ? chapterTitle : title;
            if (children != null)
            {
                foreach (var child in children.Elements("BinderItem"))
                    Walk(child, root, version, nextChapter, scenes, losses);
            }

            // A folder can carry text of its own - a chapter with an epigraph.
            var folderText = ReadDocumentText(item, root, version);
            if (folderText.Length > 0)
                scenes.Add(new ScrivenerScene(nextChapter, title, folderText, SynopsisOf(item, root, version)));
            return;
        }

        var text = ReadDocumentText(item, root, version);
        var synopsis = SynopsisOf(item, root, version);
        // A document with neither text nor a synopsis is an empty placeholder;
        // importing it would produce a scene with nothing in it.
        if (text.Length == 0 && synopsis.Length == 0) return;

        scenes.Add(new ScrivenerScene(
            chapterTitle.Length > 0 ? chapterTitle : "Imported", title, text, synopsis));
    }

    /// <summary>The document's prose, read through the RTF reader Novalist
    /// already uses for a plain .rtf import.</summary>
    private static string ReadDocumentText(XElement item, string root, string version)
    {
        var file = DocumentPath(item, root, version, "content.rtf");
        if (file == null || !File.Exists(file)) return string.Empty;

        // Read wraps the whole walk in the same catch, so a file that vanishes
        // mid-import degrades to an empty project rather than needing a second
        // guard here.
        var document = ManuscriptReader.ReadRtf(File.ReadAllText(file));
        return string.Join("\n\n", document.Paragraphs.Select(p => p.Text)).Trim();
    }

    /// <summary>The document's synopsis card, which becomes the scene's.</summary>
    private static string SynopsisOf(XElement item, string root, string version)
    {
        var file = DocumentPath(item, root, version, "synopsis.txt");
        if (file == null || !File.Exists(file)) return string.Empty;

        return File.ReadAllText(file).Trim();
    }

    /// <summary>
    /// Where one document's file lives. Scrivener 3 keys on a UUID folder;
    /// Scrivener 2 on a numeric id with the name baked into the filename, and
    /// has no separate synopsis file.
    /// </summary>
    private static string? DocumentPath(XElement item, string root, string version, string fileName)
    {
        if (version == "3")
        {
            var uuid = (string?)item.Attribute("UUID");
            return string.IsNullOrWhiteSpace(uuid)
                ? null
                : Path.Combine(root, "Files", "Data", uuid, fileName);
        }

        var id = (string?)item.Attribute("ID");
        if (string.IsNullOrWhiteSpace(id)) return null;
        // Scrivener 2 keeps only the prose under Files/Docs; synopses live in a
        // separate index the importer does not read.
        return fileName == "content.rtf"
            ? Path.Combine(root, "Files", "Docs", id + ".rtf")
            : null;
    }
}
