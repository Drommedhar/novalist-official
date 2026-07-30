using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Where a place is allowed to sit in the tree.
///
/// The hierarchy was rendered from a plain parent string and reparenting meant
/// editing an autocomplete field, so nothing ever checked whether the answer
/// made sense: a place could be made its own ancestor and the tree would
/// silently drop the whole branch, because the renderer refuses to recurse
/// forever and a cycle has no root.
///
/// The rules live here rather than in the RPC so every surface that moves a
/// place - drag, a menu, an extension - agrees about them.
/// </summary>
public static class PlaceHierarchy
{
    /// <summary>
    /// True when <paramref name="child"/> may sit under <paramref name="parentName"/>.
    ///
    /// An empty parent is always allowed: that is how a place is lifted back to
    /// the top of the tree.
    /// </summary>
    public static bool CanReparent(
        IReadOnlyList<LocationData> places, LocationData child, string? parentName)
    {
        var wanted = (parentName ?? string.Empty).Trim();
        if (wanted.Length == 0) return true;

        // A world has nothing above it - that is what makes it one.
        if (child.IsWorld) return false;

        // Its own name, whatever the case: a place cannot contain itself.
        if (string.Equals(wanted, child.Name, StringComparison.OrdinalIgnoreCase)) return false;

        var parent = places.FirstOrDefault(
            p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
        // A name nothing answers to is allowed: the tree already renders such a
        // place at the top, and refusing would block naming a parent before
        // creating it.
        if (parent == null) return true;

        return !IsDescendant(places, parent, child);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is somewhere under
    /// <paramref name="ancestor"/>. Walks upward from the candidate, which
    /// terminates on a tree and on an already-broken cycle alike.
    /// </summary>
    public static bool IsDescendant(
        IReadOnlyList<LocationData> places, LocationData candidate, LocationData ancestor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = candidate;
        while (current != null)
        {
            if (string.Equals(current.Name, ancestor.Name, StringComparison.OrdinalIgnoreCase))
                return true;
            // A cycle that is already in the file - written by an older version,
            // or by hand - must not hang the walk.
            if (!seen.Add(current.Name)) return false;

            var parentName = (current.Parent ?? string.Empty).Trim();
            if (parentName.Length == 0) return false;
            current = places.FirstOrDefault(
                p => string.Equals(p.Name, parentName, StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }
}
