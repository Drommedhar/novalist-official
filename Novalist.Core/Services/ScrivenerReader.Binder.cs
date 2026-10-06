using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public static partial class ScrivenerReader
{
    private static List<XElement> ChildrenOf(XElement item)
        => item.Element("Children")?.Elements("BinderItem").ToList() ?? [];

    /// <summary>
    /// How many documents sit anywhere beneath a row, itself included when it is
    /// one rather than a container.
    ///
    /// An empty folder is worth nothing, and saying "1 document" of the draft
    /// folder somebody has not started yet is exactly the wrong answer to give
    /// on the screen that exists because that folder is empty.
    /// </summary>
    private static int DocumentsIn(XElement item)
    {
        var children = ChildrenOf(item);
        if (children.Count > 0) return children.Sum(DocumentsIn);
        return TypeOf(item) is Folder or DraftFolder or ResearchFolder or TrashFolder ? 0 : 1;
    }

    /// <summary>The project folder, whether the caller pointed at it or at the
    /// .scrivx inside it.</summary>
    private static string? ResolveRoot(string path)
    {
        if (Directory.Exists(path)) return path;
        if (File.Exists(path)) return Path.GetDirectoryName(path);
        return null;
    }

    // ── The binder's top level ───────────────────────────────────────────

    /// <summary>
    /// Where one binder entry's contents go: what the writer said, and failing
    /// that what Scrivener's own markers say.
    ///
    /// Only the draft, the trash and the template sheets are marked, so
    /// everything else falls back to what it is nested in - a child of a
    /// research folder is research, a child of a characters folder is a
    /// character - and to research at the top level, which is where anything
    /// outside the draft used to go unconditionally.
    /// </summary>
    private static ScrivenerDestination DestinationOf(
        XElement item, XElement? draft, Context ctx,
        IReadOnlyDictionary<string, ScrivenerDestination>? mapping,
        ScrivenerDestination? inherited)
    {
        if (mapping != null && mapping.TryGetValue(KeyOf(item, ctx), out var chosen)) return chosen;

        if (draft != null && ReferenceEquals(item, draft)) return ScrivenerDestination.Manuscript;

        if (TypeOf(item) == TrashFolder) return ScrivenerDestination.Skip;

        // The template sheets are Scrivener's blank forms, not the writer's
        // filled-in ones. Importing them produces a character called "Character
        // Sketch" whose every field is a prompt.
        if (UuidOf(item) is { Length: > 0 } uuid && uuid == ctx.TemplateFolderUuid)
            return ScrivenerDestination.Skip;

        // Inside a draft, a document is a scene whatever icon it carries -
        // chasing the icon there would turn prose into a Codex entry.
        if (inherited is ScrivenerDestination.Manuscript or ScrivenerDestination.Draft
            or ScrivenerDestination.Book)
        {
            return inherited.Value;
        }

        var kind = EntityKindOf(item);
        if (kind == ScrivenerEntityKind.Character) return ScrivenerDestination.Characters;
        if (kind == ScrivenerEntityKind.Location) return ScrivenerDestination.Places;

        // A binder with no draft folder is not a Scrivener-authored project -
        // a hand-made one, or a fragment. Reading the whole binder as the
        // manuscript is right there, and wrong the moment a draft exists.
        return inherited ?? (draft == null
            ? ScrivenerDestination.Manuscript
            : ScrivenerDestination.Research);
    }

    private sealed record BinderRouting(Context Context, XElement? Draft,
        IReadOnlyDictionary<string, ScrivenerDestination>? Mapping);

    /// <summary>
    /// Sends one binder entry where it was routed, letting the level below it
    /// override where the writer named those rows individually.
    ///
    /// A folder holding nine drafts is one row and its drafts are nine, and the
    /// whole point is that they can differ - so a parent whose children were
    /// named individually contributes nothing itself and defers to them.
    /// </summary>
    private static void Route(
        XElement item, BinderRouting routing,
        ScrivenerDestination destination, int depth, string folderTag = "")
    {
        var (ctx, draft, mapping) = routing;
        var childItems = ChildrenOf(item);

        // Only the assignable rows - the top level and the one below it - can be
        // redirected. Below that the binder's own shape decides, which is what
        // parts, chapters and scenes have always been worked out from.
        if (depth == 0 && mapping != null
            && childItems.Any(c => mapping.ContainsKey(KeyOf(c, ctx))))
        {
            foreach (var child in childItems)
            {
                // The nearest folder is still what tags a research item, so a
                // row that was left as research is tagged exactly as it would
                // have been had its parent been walked whole.
                Route(child, routing,
                    DestinationOf(child, draft, ctx, mapping, destination), depth + 1,
                    TitleOf(item));
            }

            return;
        }

        switch (destination)
        {
            case ScrivenerDestination.Skip:
                ctx.Losses.Add(TitleOf(item));
                return;

            case ScrivenerDestination.Characters:
                WalkEntities(item, ScrivenerEntityKind.Character, ctx);
                return;

            case ScrivenerDestination.Places:
                WalkEntities(item, ScrivenerEntityKind.Location, ctx);
                return;

            case ScrivenerDestination.Manuscript:
                // Scrivener's own draft folder is a container - its children are
                // the book. Any other folder sent to the manuscript keeps itself,
                // so merging a second draft into an existing book arrives as the
                // part or chapter it looks like rather than losing its grouping.
                if (draft != null && ReferenceEquals(item, draft))
                {
                    foreach (var child in childItems) WalkDraft(child, ctx, Node.Empty, Node.Empty);
                }
                else
                {
                    WalkDraft(item, ctx, Node.Empty, Node.Empty);
                }

                return;

            case ScrivenerDestination.Draft:
            case ScrivenerDestination.Book:
                // The folder names the draft or the book; what is inside it is
                // that draft's manuscript, so the folder is a container exactly
                // as Scrivener's own draft folder is.
                ctx.Target = new ScrivenerTarget(
                    destination == ScrivenerDestination.Draft
                        ? ScrivenerTargetKind.Draft
                        : ScrivenerTargetKind.Book,
                    KeyOf(item, ctx),
                    TitleOf(item));
                foreach (var child in childItems) WalkDraft(child, ctx, Node.Empty, Node.Empty);
                ctx.Target = ScrivenerTarget.Manuscript;
                return;

            default:
                // A sketch filed under something the rules called research is
                // still a character; one under a folder the writer themselves
                // called research is not, because they just said so.
                WalkResearch(item, ctx, folderTag,
                    respectEntityIcons: mapping?.ContainsKey(KeyOf(item, ctx)) != true);
                return;
        }
    }

    /// <summary>The book, draft or new book a document's chapter belongs to.</summary>
    private readonly record struct ScrivenerTarget(
        ScrivenerTargetKind Kind, string Key, string Title)
    {
        public static ScrivenerTarget Manuscript
            => new(ScrivenerTargetKind.Manuscript, string.Empty, string.Empty);
    }

    // ── The draft ────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the draft, turning the first level of folders into parts when they
    /// hold folders of their own and into chapters when they do not.
    ///
    /// Scrivener nests arbitrarily and Novalist is three levels - act, chapter,
    /// scene - so anything below a chapter flattens into it. That loses nesting
    /// rather than text, which is the right way round.
    /// </summary>
    private static void WalkDraft(XElement item, Context ctx, Node part, Node chapter)
    {
        var title = TitleOf(item);
        var children = item.Element("Children");
        var childItems = children?.Elements("BinderItem").ToList() ?? [];
        var type = TypeOf(item);
        var isFolder = type is Folder or DraftFolder || childItems.Count > 0;

        if (isFolder)
        {
            Node nextPart = part, nextChapter = chapter;
            var self = new Node(KeyOf(item, ctx), title);
            if (part.IsEmpty && chapter.IsEmpty && childItems.Any(c => TypeOf(c) == Folder))
            {
                // Holds folders, and nothing above it does: a part.
                nextPart = self;
            }
            else if (chapter.IsEmpty)
            {
                nextChapter = self;
            }

            foreach (var child in childItems)
                WalkDraft(child, ctx, nextPart, nextChapter);

            // A folder can carry text of its own - a chapter with an epigraph.
            var folderText = ReadRtf(item, ctx, "content.rtf", ".rtf");
            if (!folderText.IsEmpty)
                AddScene(item, ctx, nextPart, nextChapter.IsEmpty ? self : nextChapter, title, folderText);

            return;
        }

        AddScene(item, ctx, part, chapter.IsEmpty ? ctx.LooseChapter : chapter, title,
            ReadRtf(item, ctx, "content.rtf", ".rtf"));
    }

    /// <summary>A part or chapter the walk is inside: its binder identity and
    /// the title to show for it.</summary>
    private readonly record struct Node(string Key, string Title)
    {
        public static Node Empty => new(string.Empty, string.Empty);

        /// <summary>Key prefix for the chapter a draft document lands in when the
        /// binder gave it no folder. Completed per book or draft by
        /// <see cref="Context.LooseChapter"/> - a shared key put every loose
        /// document from every draft into one chapter owned by whichever draft
        /// reached it first, and left the rest of them empty.</summary>
        public const string LooseChapterKey = "\u0000loose";

        public bool IsEmpty => Key.Length == 0;
    }

    /// <summary>A binder item's identity: its UUID in Scrivener 3, its numeric
    /// ID in Scrivener 2.</summary>
    private static string KeyOf(XElement item, Context ctx)
    {
        var key = ctx.Version == "3"
            ? UuidOf(item)
            : ((string?)item.Attribute("ID") ?? string.Empty).Trim();
        // A folder with no identity at all still has to be distinguishable from
        // the next one, or two untitled chapters merge.
        return key.Length > 0 ? key : "\u0000" + item.GetHashCode().ToString();
    }
}
