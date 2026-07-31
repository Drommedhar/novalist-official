namespace Novalist.Core.Services;

/// <summary>What one person is to another.</summary>
public enum KinshipKind
{
    /// <summary>No line of descent connects them.</summary>
    Unrelated,
    Self,
    /// <summary>Parent, grandparent, great-grandparent: <c>Degree</c> counts the steps.</summary>
    Ancestor,
    /// <summary>Child, grandchild, great-grandchild.</summary>
    Descendant,
    Sibling,
    /// <summary>Aunt or uncle, great-aunt or great-uncle: <c>Degree</c> counts the greats plus one.</summary>
    AuntUncle,
    /// <summary>Niece or nephew, great-niece or great-nephew.</summary>
    NieceNephew,
    /// <summary>First cousin, second cousin, once removed: see <c>Degree</c> and <c>Removed</c>.</summary>
    Cousin
}

/// <summary>
/// How two people are related, as numbers rather than words.
/// </summary>
/// <param name="Degree">
/// Generations for an ancestor, descendant, aunt or niece; the cousin number
/// for a cousin. Zero where it does not apply.
/// </param>
/// <param name="Removed">Generations of difference between cousins.</param>
public sealed record KinshipResult(KinshipKind Kind, int Degree, int Removed)
{
    public static readonly KinshipResult Unrelated = new(KinshipKind.Unrelated, 0, 0);
}

/// <summary>
/// Works out that somebody is a great-aunt, or a second cousin once removed,
/// from parentage alone.
///
/// Novalist stores a relationship as a role and a target - "mother", "Mira" -
/// and can draw the lines, but nothing could answer the question a writer
/// actually asks in the middle of a scene, which is how these two are related.
/// Deriving it means the writer records parents once rather than recording
/// every pair.
///
/// The words belong to the caller. This returns the shape of the relationship
/// and nothing else, so the same answer reads correctly in every language the
/// interface speaks.
/// </summary>
public static class Kinship
{
    /// <summary>
    /// How <paramref name="fromId"/> is related to <paramref name="toId"/> -
    /// read as "from is the ... of to".
    /// </summary>
    /// <param name="parents">Each person's parents, by id.</param>
    public static KinshipResult Describe(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> parents,
        string fromId,
        string toId)
    {
        if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            return KinshipResult.Unrelated;
        if (string.Equals(fromId, toId, StringComparison.Ordinal))
            return new KinshipResult(KinshipKind.Self, 0, 0);

        var fromLine = Ancestry(parents, fromId);
        var toLine = Ancestry(parents, toId);

        // The nearest shared ancestor. Nearest by total distance, because a
        // family where two people share both a grandparent and a great-uncle
        // should read as the closer of the two.
        var bestTotal = int.MaxValue;
        var bestUp = 0;
        var bestDown = 0;
        foreach (var (ancestor, up) in fromLine)
        {
            if (!toLine.TryGetValue(ancestor, out var down)) continue;
            // Compared as one total: two sentinels added together overflow to a
            // negative, and nothing is ever nearer than that.
            if (up + down >= bestTotal) continue;
            bestTotal = up + down;
            bestUp = up;
            bestDown = down;
        }

        if (bestTotal == int.MaxValue) return KinshipResult.Unrelated;
        return Classify(bestUp, bestDown);
    }

    /// <summary>
    /// Turns the two distances to a shared ancestor into a name for the
    /// relationship. This is the standard canonical kinship table.
    /// </summary>
    /// <param name="up">Steps from the first person up to the shared ancestor.</param>
    /// <param name="down">Steps from the second person up to the same ancestor.</param>
    internal static KinshipResult Classify(int up, int down)
    {
        // One of them is the shared ancestor, so the line is direct.
        if (up == 0) return new KinshipResult(KinshipKind.Ancestor, down, 0);
        if (down == 0) return new KinshipResult(KinshipKind.Descendant, up, 0);

        if (up == 1 && down == 1) return new KinshipResult(KinshipKind.Sibling, 0, 0);

        // A sibling of an ancestor, or the reverse. The degree counts the
        // greats: an aunt is 1, a great-aunt 2.
        if (up == 1) return new KinshipResult(KinshipKind.AuntUncle, down - 1, 0);
        if (down == 1) return new KinshipResult(KinshipKind.NieceNephew, up - 1, 0);

        // Everything else is a cousin. The number is how far the nearer of the
        // two is from the shared ancestor; the removal is the difference.
        return new KinshipResult(KinshipKind.Cousin, Math.Min(up, down) - 1, Math.Abs(up - down));
    }

    /// <summary>
    /// Everyone above a person, with how many generations up they are - the
    /// person themselves at zero.
    ///
    /// Breadth-first and visit-once, so a cycle in hand-entered parentage
    /// (somebody recorded as their own grandparent) terminates rather than
    /// hanging the app.
    /// </summary>
    private static Dictionary<string, int> Ancestry(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> parents, string id)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal) { [id] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!parents.TryGetValue(current, out var above)) continue;
            foreach (var parent in above)
            {
                if (string.IsNullOrEmpty(parent) || seen.ContainsKey(parent)) continue;
                seen[parent] = seen[current] + 1;
                queue.Enqueue(parent);
            }
        }

        return seen;
    }
}
