using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Starting points for the types a worldbuilder ends up needing. The builder is
/// an empty form, so everybody rebuilds the same field list by hand and
/// rebuilds it differently every project.
/// </summary>
public class GenreTypePacksTests
{
    [Fact]
    public void ThePacksTheAuditNamesAllShip()
    {
        var keys = GenreTypePacks.All.Select(p => p.TypeKey).ToList();

        Assert.Contains("species", keys);
        Assert.Contains("magic_system", keys);
        Assert.Contains("faction", keys);
        Assert.Contains("language", keys);
    }

    [Fact]
    public void EveryPackIsUsableAsItStands()
    {
        Assert.All(GenreTypePacks.All, pack =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pack.TypeKey));
            Assert.False(string.IsNullOrWhiteSpace(pack.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(pack.DisplayNamePlural));
            Assert.False(string.IsNullOrWhiteSpace(pack.FolderName));
            // A pack with no fields is an empty form with extra steps.
            Assert.NotEmpty(pack.DefaultFields);
            Assert.Equal("user", pack.Source);
        });
    }

    [Fact]
    public void EveryFieldSaysWhatBelongsInIt()
    {
        // "Cost" answers nothing on its own. The prompt is what makes the field
        // worth filling in, and it stays on the entry afterwards rather than
        // vanishing with the creation wizard.
        //
        // Most are questions; a few are better as statements - "The handful you
        // will actually put in the prose" - so the guarantee is that there is
        // real guidance, not that it ends in a question mark.
        Assert.All(GenreTypePacks.All.SelectMany(p => p.DefaultFields), field =>
        {
            Assert.False(string.IsNullOrWhiteSpace(field.DisplayName));
            Assert.True(field.Prompt.Trim().Length > 20, field.DisplayName);
        });
    }

    [Fact]
    public void FieldKeysAreLowercaseSnakeAndUniqueWithinAPack()
    {
        Assert.All(GenreTypePacks.All, pack =>
        {
            var keys = pack.DefaultFields.Select(f => f.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
            Assert.All(keys, key => Assert.Equal(key.ToLowerInvariant(), key));
        });
    }

    [Fact]
    public void NoPackTurnsOnAnImageStripNobodyWillFill()
    {
        // These are things a world runs on rather than things with a face.
        Assert.All(GenreTypePacks.All, pack =>
        {
            Assert.False(pack.Features.IncludeImages);
            Assert.True(pack.Features.IncludeSections);
            Assert.True(pack.Features.IncludeRelationships);
        });
    }

    [Theory]
    [InlineData("Public face", "public_face")]
    [InlineData("What it looks like", "what_it_looks_like")]
    [InlineData("  Cost  ", "cost")]
    public void LabelsBecomeTheKeysTheBuilderWouldHaveMade(string label, string expected)
        => Assert.Equal(expected, GenreTypePacks.Slug(label));

    [Fact]
    public void TheIconIsNotAPictograph()
    {
        // The icon system is SVG paths and lucide names; the default here was
        // the only emoji left in the entity model.
        Assert.Equal(string.Empty, new CustomEntityTypeDefinition().Icon);
    }
}
