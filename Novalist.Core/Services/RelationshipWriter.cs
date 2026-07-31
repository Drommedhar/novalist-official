using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One relationship as it is written: what this entry is to that one.</summary>
/// <param name="InverseRole">
/// What the other entry is back. Empty means the caller does not know, and the
/// other side is left alone rather than guessed at.
/// </param>
public sealed record RelationshipRow(
    string Role,
    string Target,
    string? Category = null,
    string? InverseRole = null);

/// <summary>
/// What a relationship write touched besides the entry it was written on.
/// </summary>
/// <param name="Changed">
/// Entries that gained an inverse row and need saving. The subject is not in
/// here: the caller already has it.
/// </param>
/// <param name="Pairs">
/// Role pairs worth remembering, so the next row offers the inverse instead of
/// asking for it to be typed the same way twice.
/// </param>
public sealed record RelationshipWriteResult(
    IReadOnlyList<IEntityData> Changed,
    IReadOnlyList<(string Role, string Inverse)> Pairs);

/// <summary>
/// Writes an entry's relationships, and the other half of each one onto the
/// entry it names.
///
/// A relationship that exists from one side only is worse than none: the graph
/// draws an edge that vanishes when you look from the other end, and a reader
/// of the second entry has no way to know the first one claims anything. So
/// the write-back is not a nicety, and it is not the editor's job either -
/// which is why it lives here rather than in the RPC that used to own it.
/// An extension writing a relationship gets the same rule the Codex applies.
///
/// The subject is mutated in place; every entry is saved by the caller, which
/// is the only thing that knows how each kind is persisted.
/// </summary>
public static class RelationshipWriter
{
    /// <param name="everything">
    /// Every entry in the project, of every type. A relationship row names a
    /// thing, not a character - the target is as likely to be a ship or a
    /// house as a person.
    /// </param>
    public static RelationshipWriteResult Apply(
        IEntityData subject,
        string subjectName,
        IReadOnlyList<RelationshipRow> rows,
        IReadOnlyList<IEntityData> everything)
    {
        subject.Relationships = [.. rows
            // A row with neither a role nor a target is an empty line somebody
            // tabbed through, not a relationship.
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new EntityRelationship
            {
                Role = (r.Role ?? string.Empty).Trim(),
                Target = (r.Target ?? string.Empty).Trim(),
                Category = (r.Category ?? string.Empty).Trim()
            })];

        var changed = new List<IEntityData>();
        var pairs = new List<(string, string)>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Role)
                || string.IsNullOrWhiteSpace(row.Target)
                || string.IsNullOrWhiteSpace(row.InverseRole))
                continue;

            var target = everything.FirstOrDefault(e =>
                string.Equals(e.DisplayName, row.Target.Trim(), StringComparison.OrdinalIgnoreCase));
            // An entry relating to itself would write its own inverse onto
            // itself, which reads as two relationships and is one confusion.
            if (target == null || string.Equals(target.Id, subject.Id, StringComparison.Ordinal))
                continue;

            var already = target.Relationships.Any(r =>
                string.Equals(r.Role, row.InverseRole.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Target, subjectName, StringComparison.OrdinalIgnoreCase));
            if (!already)
            {
                target.Relationships.Add(new EntityRelationship
                {
                    Role = row.InverseRole.Trim(),
                    Target = subjectName
                });
                if (!changed.Any(c => string.Equals(c.Id, target.Id, StringComparison.Ordinal)))
                    changed.Add(target);
            }

            pairs.Add((row.Role.Trim(), row.InverseRole.Trim()));
        }

        return new RelationshipWriteResult(changed, pairs);
    }

}
