using Novalist.Core.Models;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class PovDetectorTests
{
    [Fact]
    public void Detect_EmptyContent_ReturnsEmpty()
        => Assert.Equal(string.Empty, PovDetector.Detect("  ", new List<CharacterData>()));

    [Fact]
    public void Detect_NamedCharacter_ReturnsDisplayName()
    {
        var chars = new List<CharacterData> { new() { Name = "Alice" } };
        var content = "Alice walked in. Alice sat down. Bob waved at Alice.";
        Assert.Equal("Alice", PovDetector.Detect(content, chars));
    }

    [Fact]
    public void Detect_PicksMostFrequentCharacter()
    {
        var chars = new List<CharacterData>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" }
        };
        var content = "Bob spoke. Alice replied. Alice laughed. Alice left.";
        Assert.Equal("Alice", PovDetector.Detect(content, chars));
    }

    [Fact]
    public void Detect_FirstPerson_WhenNoCharacterMatchAndEnoughPronouns()
    {
        var chars = new List<CharacterData> { new() { Name = "Zzzz" } };
        var content = "I walked. I saw the door. My hand reached out. We entered.";
        Assert.Equal("First person", PovDetector.Detect(content, chars));
    }

    [Fact]
    public void Detect_FirstPerson_UsesCustomLabel()
    {
        var content = "I am here. I know it. My choice. We agree.";
        Assert.Equal("Ich", PovDetector.Detect(content, new List<CharacterData>(), "Ich"));
    }

    [Fact]
    public void Detect_NoMatchAndFewPronouns_ReturnsEmpty()
    {
        var content = "The door opened slowly without a sound.";
        Assert.Equal(string.Empty, PovDetector.Detect(content, new List<CharacterData>()));
    }

    [Fact]
    public void Detect_SkipsBlankAliases()
    {
        // Character with only whitespace name yields no aliases; should not match.
        var chars = new List<CharacterData> { new() { Name = "   " } };
        var content = "The forest was quiet and still.";
        Assert.Equal(string.Empty, PovDetector.Detect(content, chars));
    }

    [Fact]
    public void Detect_MatchesSurnameAlias()
    {
        var chars = new List<CharacterData> { new() { Name = "Jon", Surname = "Snow" } };
        var content = "Snow rode north. Snow drew his sword. Snow did not flinch.";
        Assert.Equal("Jon Snow", PovDetector.Detect(content, chars));
    }
}
