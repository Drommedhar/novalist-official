using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// Files kept with a Codex entry.
///
/// Entries held images and nothing else, so a recorded interview or a scanned
/// deed had to be filed as a Research item and linked back - stored and
/// surfaced somewhere other than the entry it is about.
/// </summary>
public class EntityAttachmentTests
{
    [Theory]
    [InlineData("interview.mp3", AttachmentKind.Audio)]
    [InlineData("name.M4A", AttachmentKind.Audio)]
    [InlineData("walkthrough.mp4", AttachmentKind.Video)]
    [InlineData("clip.webm", AttachmentKind.Video)]
    [InlineData("deed.pdf", AttachmentKind.Document)]
    [InlineData("notes.md", AttachmentKind.Document)]
    public void TheKindComesFromTheExtension(string fileName, AttachmentKind expected)
        => Assert.Equal(expected, AttachmentKinds.Of(fileName));

    [Theory]
    [InlineData("mystery.qqq")]
    [InlineData("nofileextension")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsSimplyAFile(string? fileName)
    {
        // An unknown format still attaches and still opens; only the icon is
        // less specific, which costs nothing.
        Assert.Equal(AttachmentKind.File, AttachmentKinds.Of(fileName));
    }

    [Fact]
    public void TwoAttachmentsMadeAtOnceAreTellableApart()
    {
        var first = new EntityAttachment { Name = "The deed" };
        var second = new EntityAttachment { Name = "The recording" };

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void APlainFileIsNotALink()
    {
        var file = new EntityAttachment { Path = "Attachments/deed.pdf", Kind = AttachmentKind.Document };

        Assert.False(file.IsLink);
        Assert.Equal(string.Empty, file.Url);
    }

    [Fact]
    public void ALinkSaysSo()
    {
        // Nothing was copied for a link, and pretending to have saved the page
        // would be a promise this cannot keep.
        var link = new EntityAttachment { Url = "https://example.test", Kind = AttachmentKind.Link };

        Assert.True(link.IsLink);
        Assert.Equal(string.Empty, link.Path);
    }

    [Fact]
    public void AnEntryStartsWithNothingAttached()
    {
        Assert.Empty(new CharacterData().Attachments);
        Assert.Empty(new LocationData().Attachments);
        Assert.Empty(new ItemData().Attachments);
        Assert.Empty(new LoreData().Attachments);
        Assert.Empty(new CustomEntityData().Attachments);
    }

    [Fact]
    public void AnAttachmentCanCarryAReasonForBeingThere()
    {
        var attachment = new EntityAttachment { Note = "Settles who owns the house." };

        Assert.Equal("Settles who owns the house.", attachment.Note);
        Assert.Equal(AttachmentKind.File, attachment.Kind);
    }
}
