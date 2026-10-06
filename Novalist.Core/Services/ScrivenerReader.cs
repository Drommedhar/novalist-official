using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>
/// Reads a Scrivener project from its folder or exact binder manifest.
///
/// Both layouts are handled because both are in the wild: Scrivener 2 numbers
/// its documents and keeps them in <c>Files/Docs/&lt;id&gt;.rtf</c>, Scrivener 3
/// gives each a UUID folder under <c>Files/Data/</c>. The binder tree in the
/// <c>.scrivx</c> is the same shape in both, which is what makes one reader
/// enough.
///
/// What the binder means is read from its <c>Type</c> attributes and its icon
/// names, never from the titles. An earlier reader matched the strings
/// "Research", "Trash" and "Front Matter", which is wrong twice over: those
/// titles are the writer's to change and are already translated in a
/// non-English Scrivener, and nothing at all marked the draft. So the draft
/// folder - titled "Manuscript" in the stock template - looked like an ordinary
/// chapter folder, and every part, chapter and scene beneath it collapsed into
/// one chapter called Manuscript, while the template's own instruction sheet
/// was imported as prose.
///
/// The import is still lossy and still says so. Snapshots, collections, compile
/// settings and label colours have no equivalent here, and pretending otherwise
/// would leave a writer discovering the gaps one at a time.
/// </summary>
public static partial class ScrivenerReader
{
    public const string ProjectExtension = ".scriv";

    public const string BinderExtension = ".scrivx";

    /// <summary>The chapter a draft document lands in when the binder gave it
    /// no folder of its own.</summary>
    public const string DefaultChapterTitle = "Imported";

    // Binder Type attributes that mark a container Scrivener owns rather than
    // the writer. These are stable identifiers, unlike the titles beside them.
    private const string DraftFolder = "DraftFolder";
    private const string ResearchFolder = "ResearchFolder";
    private const string TrashFolder = "TrashFolder";
    private const string Folder = "Folder";

    /// <summary>Whether a path should be handled as a Scrivener project. A
    /// selected directory is a Scrivener candidate regardless of its name,
    /// because Scrivenix projects do not necessarily carry a .scriv suffix and
    /// no other built-in manuscript format is directory-based.</summary>
    public static bool LooksLikeScrivener(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path)) return true;
        var extension = Path.GetExtension(path);
        return extension.Equals(BinderExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a project. Returns an empty result rather than throwing for
    /// anything unreadable: an import that cannot start should say so in the
    /// dialog, not crash the app.
    /// </summary>
    public static ScrivenerProject Read(string path) => Read(path, null, null);

    /// <summary>
    /// Reads a project, sending the binder rows named in <paramref name="mapping"/>
    /// where the writer asked rather than where the rules would have put them.
    ///
    /// A row the mapping does not name keeps the destination it would have had,
    /// so a partial mapping is meaningful and an absent one imports exactly as
    /// before. Keys are the binder identities <see cref="Outline"/> reported.
    /// </summary>
    public static ScrivenerProject Read(
        string path, IReadOnlyDictionary<string, ScrivenerDestination>? mapping)
        => Read(path, mapping, null);

    /// <summary>
    /// Reads a project and reports content-safe failure details to
    /// <paramref name="diagnostic"/>. Raw exception messages never reach the
    /// callback because XML errors can repeat user-authored element names.
    /// </summary>
    public static ScrivenerProject Read(
        string path,
        IReadOnlyDictionary<string, ScrivenerDestination>? mapping,
        Action<ScrivenerReadDiagnostic>? diagnostic)
    {
        try
        {
            var opened = Open(path, diagnostic);
            if (opened == null) return new ScrivenerProject();

            var (_, binder, ctx) = opened.Value;
            var top = binder.Elements("BinderItem").ToList();
            // A binder with no draft folder is not a Scrivener-authored project -
            // a hand-made one, or a fragment. Reading the whole binder as the
            // manuscript is right there, and wrong the moment a draft exists.
            var draft = top.FirstOrDefault(i => TypeOf(i) == DraftFolder);
            var routing = new BinderRouting(ctx, draft, mapping);

            foreach (var item in top)
            {
                Route(item, routing,
                    DestinationOf(item, draft, ctx, mapping, inherited: null), depth: 0);
            }

            var project = new ScrivenerProject
            {
                Scenes = ctx.Scenes,
                Entities = ctx.Entities,
                Research = ctx.Research,
                CustomFields = ctx.CustomFields,
                Version = ctx.Version,
                Losses = [.. ctx.Losses.Distinct(StringComparer.Ordinal)]
            };
            if (project.IsEmpty)
                Report(diagnostic, "read", "no-importable-content");
            return project;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            Report(diagnostic, "content", "read-failed", ex);
            return new ScrivenerProject();
        }
    }

    /// <summary>
    /// The binder rows the writer can redirect, each carrying where the rules
    /// would send it. Reads the binder only - no document is opened, so this is
    /// cheap enough to run the moment a folder is chosen.
    ///
    /// Empty for anything that is not a readable Scrivener project, which the
    /// dialog shows as a project it could not read rather than as an error.
    /// </summary>
    public static IReadOnlyList<ScrivenerBinderRow> Outline(string path)
        => Outline(path, null);

    /// <summary>Reads the redirectable binder rows and reports content-free
    /// failure details through <paramref name="diagnostic"/>.</summary>
    public static IReadOnlyList<ScrivenerBinderRow> Outline(
        string path, Action<ScrivenerReadDiagnostic>? diagnostic)
    {
        var opened = Open(path, diagnostic);
        if (opened == null) return [];

        var (_, binder, ctx) = opened.Value;
        var top = binder.Elements("BinderItem").ToList();
        if (top.Count == 0)
        {
            Report(diagnostic, "binder", "empty");
            return [];
        }

        var draft = top.FirstOrDefault(i => TypeOf(i) == DraftFolder);
        var rows = new List<ScrivenerBinderRow>();

        foreach (var item in top)
        {
            var destination = DestinationOf(item, draft, ctx, mapping: null, inherited: null);
            var children = ChildrenOf(item);
            rows.Add(new ScrivenerBinderRow(
                KeyOf(item, ctx), TitleOf(item), 0, destination,
                DocumentsIn(item), children.Count > 0));

            foreach (var child in children)
            {
                rows.Add(new ScrivenerBinderRow(
                    KeyOf(child, ctx),
                    TitleOf(child),
                    1,
                    DestinationOf(child, draft, ctx, mapping: null, inherited: destination),
                    DocumentsIn(child),
                    ChildrenOf(child).Count > 0));
            }
        }

        return rows;
    }

    /// <summary>Opens the project's binder, or null when there is nothing to read.</summary>
    private static (XDocument Document, XElement Binder, Context Context)? Open(
        string path, Action<ScrivenerReadDiagnostic>? diagnostic)
    {
        try
        {
            var root = ResolveRoot(path);
            if (root == null)
            {
                Report(diagnostic, "path", "not-found");
                return null;
            }

            var resolvedBinder = ResolveBinderFile(path, root);
            var scrivx = resolvedBinder.Path;
            if (scrivx == null)
            {
                Report(diagnostic, "manifest", resolvedBinder.FailureReason);
                return null;
            }

            XDocument document;
            try
            {
                document = LoadXml(scrivx);
            }
            catch (System.Xml.XmlException ex)
            {
                Report(diagnostic, "manifest", "invalid-xml", ex);
                return null;
            }

            var binder = document.Descendants("Binder").FirstOrDefault();
            if (binder == null)
            {
                Report(diagnostic, "binder", "not-found");
                return null;
            }

            // Scrivener itself uses these exact names, but a project produced
            // under Wine can live on a case-sensitive Linux filesystem after
            // travelling through sync or archive tools. Resolve every package
            // component without assuming its casing survived that trip.
            var files = FindDirectory(root, "Files");
            var data = FindDirectory(files, "Data");
            var docs = FindDirectory(files, "Docs");

            // Scrivener 3 keys binder entries and Data folders by UUID;
            // Scrivener 2 uses numeric IDs under Docs. The binder attributes are
            // the fallback when only the .scrivx was copied and no payload
            // directory is present yet.
            var version = data != null || (docs == null
                && binder.Descendants("BinderItem").Any(i => UuidOf(i).Length > 0))
                ? "3"
                : "2";
            return (document, binder, new Context(files, data, docs, version, document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(diagnostic, "package", "access-failed", ex);
            return null;
        }
        catch (System.Xml.XmlException ex)
        {
            Report(diagnostic, "supporting-files", "invalid-xml", ex);
            return null;
        }
    }

    /// <summary>The manifest the caller selected, or the best manifest in a
    /// selected project folder. Selecting a .scrivx must use that exact file:
    /// choosing an arbitrary sibling made conflict copies and adjacent projects
    /// import the wrong binder. A selected folder is accepted only when it has
    /// one manifest; its name is never used to choose between several files.</summary>
    private static (string? Path, string FailureReason) ResolveBinderFile(
        string path,
        string root)
    {
        if (File.Exists(path))
            return Path.GetExtension(path).Equals(BinderExtension, StringComparison.OrdinalIgnoreCase)
                ? (path, string.Empty)
                : (null, "not-found");

        var candidates = Directory.EnumerateFiles(root)
            .Where(file => Path.GetExtension(file).Equals(
                BinderExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
            return (null, "not-found");
        if (candidates.Count == 1)
            return (candidates[0], string.Empty);
        return (null, "ambiguous");
    }

    private static XDocument LoadXml(string path)
    {
        var document = XDocument.Load(path);
        // Scrivener's manifests are normally unnamespaced. Some XML tools add
        // a default namespace while copying or repairing one; local names still
        // carry the complete Scrivener vocabulary, so tolerate that harmless
        // wrapper rather than reporting an empty project.
        foreach (var element in document.Descendants().Where(e => e.Name.NamespaceName.Length > 0))
            element.Name = element.Name.LocalName;
        return document;
    }

    private static string? FindDirectory(string? parent, string name)
    {
        if (parent == null || !Directory.Exists(parent)) return null;
        var exact = Path.Combine(parent, name);
        return Directory.Exists(exact) ? exact : Directory.EnumerateDirectories(parent).FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFile(string? parent, string name)
    {
        if (parent == null || !Directory.Exists(parent)) return null;
        var exact = Path.Combine(parent, name);
        return File.Exists(exact) ? exact : Directory.EnumerateFiles(parent).FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void Report(
        Action<ScrivenerReadDiagnostic>? diagnostic,
        string stage,
        string reason,
        string exceptionType = "")
        => diagnostic?.Invoke(new ScrivenerReadDiagnostic(stage, reason, exceptionType));

    private static void Report(
        Action<ScrivenerReadDiagnostic>? diagnostic,
        string stage,
        string reason,
        Exception exception)
    {
        var xml = exception as System.Xml.XmlException;
        diagnostic?.Invoke(new ScrivenerReadDiagnostic(
            stage,
            reason,
            exception.GetType().Name,
            SafeExceptionDetail(exception),
            xml?.LineNumber ?? 0,
            xml?.LinePosition ?? 0,
            exception.HResult));
    }

    /// <summary>
    /// Converts runtime wording into a useful fixed explanation. Raw XML
    /// messages are not safe to redact heuristically: an unexpected-EOF error
    /// can list every still-open element without quoting any of their names.
    /// </summary>
    internal static string SafeExceptionDetail(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
            return "The operating system denied access to a project file.";
        if (exception is IOException)
            return "The operating system could not read a project file; it may be locked or unavailable.";
        if (exception is not System.Xml.XmlException xml)
            return "The project could not be read.";

        var message = xml.Message.ToLowerInvariant();
        if (message.Contains("unexpected end of file", StringComparison.Ordinal))
            return "The XML ended before one or more elements were closed.";
        if (message.Contains("does not match the end tag", StringComparison.Ordinal))
            return "An XML start tag does not match its end tag.";
        if (message.Contains("undeclared entity", StringComparison.Ordinal))
            return "The XML references an entity that was not declared.";
        if (message.Contains("undeclared prefix", StringComparison.Ordinal))
            return "The XML uses a namespace prefix that was not declared.";
        if (message.Contains("root element is missing", StringComparison.Ordinal))
            return "The XML root element is missing.";
        if (message.Contains("multiple root elements", StringComparison.Ordinal))
            return "The XML contains more than one root element.";
        if (message.Contains("data at the root level is invalid", StringComparison.Ordinal))
            return "Data at the XML root is invalid.";
        if (message.Contains("name cannot begin", StringComparison.Ordinal)
            || message.Contains("name was started with an invalid", StringComparison.Ordinal))
            return "An XML name begins with a character XML does not allow.";
        if (message.Contains("invalid character", StringComparison.Ordinal)
            || message.Contains("cannot be included in a name", StringComparison.Ordinal))
            return "The XML contains a character XML does not allow.";
        if (message.Contains("dtd is prohibited", StringComparison.Ordinal))
            return "The XML contains a DTD, which this parser does not allow.";
        if (message.Contains("unclosed literal string", StringComparison.Ordinal))
            return "The XML contains an unterminated quoted value.";
        if (message.Contains("unexpected token", StringComparison.Ordinal))
            return "The XML contains an unexpected token.";
        return "The XML parser rejected the document; source-specific details were redacted.";
    }

    /// <summary>A file-backed research item, whose bytes the import copies.</summary>
    private static void AddFile(
        XElement item, Context ctx, string title, ScrivenerResearchKind kind,
        string folderTag, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var file = DocumentPath(item, ctx, "content." + extension, "." + extension);
            if (file != null && File.Exists(file))
            {
                ctx.Research.Add(new ScrivenerResearch(
                    title, kind, string.Empty, string.Empty, file, folderTag));
                return;
            }
        }
    }

    // ── Binder vocabulary ────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> EmptyFields =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
