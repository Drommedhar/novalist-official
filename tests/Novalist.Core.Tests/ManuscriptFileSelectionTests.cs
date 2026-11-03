using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests;

public sealed class ManuscriptFileSelectionTests
{
    [Theory]
    [InlineData("Novel.scrivx")]
    [InlineData("NOVEL.SCRIVX")]
    [InlineData("Novel.scriv")]
    public void SingleFilePickerRejectsScrivenerBeforeImport(string name)
        => Assert.Throws<NotSupportedException>(() => ManuscriptFileSelection.RequireStandalone(name));

    [Theory]
    [InlineData("Novel.docx")]
    [InlineData("Novel.rtf")]
    [InlineData("Novel.md")]
    public void SingleFileManuscriptsRemainAvailable(string name)
        => ManuscriptFileSelection.RequireStandalone(name);
}
