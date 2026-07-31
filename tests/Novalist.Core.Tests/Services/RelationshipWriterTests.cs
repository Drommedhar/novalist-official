using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The rule that a relationship is written on both entries. Pulled out of the
/// RPC so the Codex and an extension get the same one; these are the cases
/// where "written" and "written on both sides" differ.
/// </summary>
public class RelationshipWriterTests
{
    private static CharacterData Person(string name, string surname = "")
        => new() { Id = Guid.NewGuid().ToString(), Name = name, Surname = surname };

    [Fact]
    public void TheSubjectsOwnRowsAreReplacedWholesale()
    {
        var subject = Person("Mara");
        subject.Relationships.Add(new EntityRelationship { Role = "old", Target = "Gone" });

        RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "Liam")], [subject]);

        Assert.Single(subject.Relationships);
        Assert.Equal("mother", subject.Relationships[0].Role);
        Assert.Equal("Liam", subject.Relationships[0].Target);
    }

    [Fact]
    public void RowsWithNeitherARoleNorATargetAreDropped()
    {
        // An empty line somebody tabbed through is not a relationship.
        var subject = Person("Mara");

        RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("", ""), new RelationshipRow("  ", "   ")], [subject]);

        Assert.Empty(subject.Relationships);
    }

    [Fact]
    public void RolesAndTargetsAreTrimmed()
    {
        var subject = Person("Mara");

        RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("  mother ", " Liam ", "  family ")], [subject]);

        Assert.Equal("mother", subject.Relationships[0].Role);
        Assert.Equal("Liam", subject.Relationships[0].Target);
        Assert.Equal("family", subject.Relationships[0].Category);
    }

    [Fact]
    public void AnInverseRoleAuthorsTheOtherHalf()
    {
        var subject = Person("Mara");
        var liam = Person("Liam");

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "Liam", null, "son")], [subject, liam]);

        Assert.Contains(liam.Relationships, r => r.Role == "son" && r.Target == "Mara");
        Assert.Contains(result.Changed, c => c.Id == liam.Id);
        Assert.Contains(("mother", "son"), result.Pairs);
    }

    [Fact]
    public void WithoutAnInverseRoleTheOtherEntryIsLeftAlone()
    {
        // Guessing what the far side is called is worse than leaving it empty:
        // a wrong role reads as a fact the writer never wrote.
        var subject = Person("Mara");
        var liam = Person("Liam");

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "Liam")], [subject, liam]);

        Assert.Empty(liam.Relationships);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Pairs);
    }

    [Fact]
    public void TheFarSideIsFoundByItsWholeName()
    {
        // A character is two fields, and a row stores the name as displayed.
        var subject = Person("Mara");
        var liam = Person("Liam", "Cole");

        RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "liam cole", null, "son")], [subject, liam]);

        Assert.Contains(liam.Relationships, r => r.Role == "son");
    }

    [Fact]
    public void AnyKindOfEntryCanBeTheFarSide()
    {
        // A relationship names a thing, not a character: an item's owner has to
        // be authored on the item as much as on the person.
        var subject = Person("Mara");
        var sword = new ItemData { Id = Guid.NewGuid().ToString(), Name = "Dawnedge" };

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("owner", "Dawnedge", null, "owned by")], [subject, sword]);

        Assert.Contains(sword.Relationships, r => r.Role == "owned by" && r.Target == "Mara");
        Assert.Contains(result.Changed, c => c.Id == sword.Id);
    }

    [Fact]
    public void ATargetTheProjectDoesNotHaveIsStillWrittenOnTheSubject()
    {
        // The row is what the writer meant; the entry may be created later.
        var subject = Person("Mara");

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "Nobody", null, "son")], [subject]);

        Assert.Single(subject.Relationships);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public void AnEntryRelatingToItselfDoesNotWriteItsOwnInverse()
    {
        var subject = Person("Mara");

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("rival", "Mara", null, "rival")], [subject]);

        Assert.Single(subject.Relationships);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public void AnInverseAlreadyThereIsNotAddedTwice()
    {
        var subject = Person("Mara");
        var liam = Person("Liam");
        liam.Relationships.Add(new EntityRelationship { Role = "Son", Target = "mara" });

        var result = RelationshipWriter.Apply(subject, "Mara",
            [new RelationshipRow("mother", "Liam", null, "son")], [subject, liam]);

        Assert.Single(liam.Relationships);
        Assert.Empty(result.Changed);
        // The pair is still worth learning even when nothing had to be written.
        Assert.Contains(("mother", "son"), result.Pairs);
    }

    [Fact]
    public void OneTargetNamedTwiceIsSavedOnce()
    {
        var subject = Person("Mara");
        var liam = Person("Liam");

        var result = RelationshipWriter.Apply(subject, "Mara",
            [
                new RelationshipRow("mother", "Liam", null, "son"),
                new RelationshipRow("teacher", "Liam", null, "student")
            ],
            [subject, liam]);

        Assert.Equal(2, liam.Relationships.Count);
        Assert.Single(result.Changed);
    }
}
