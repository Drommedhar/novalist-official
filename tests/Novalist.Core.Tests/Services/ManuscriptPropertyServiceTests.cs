using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Fields the writer adds to scenes and chapters: what is accepted as a
/// definition, what is accepted as a value, and what happens to values when
/// the field they belong to is deleted.
/// </summary>
public sealed class ManuscriptPropertyServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly ManuscriptPropertyService _sut;
    private readonly ChapterData _chapter;
    private readonly SceneData _scene;

    public ManuscriptPropertyServiceTests()
    {
        _projects.CreateProjectAsync(_dir.Path, "Props", "Book").GetAwaiter().GetResult();
        _chapter = _projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _scene = _projects.CreateSceneAsync(_chapter.Guid, "Opening").GetAwaiter().GetResult();
        _sut = new ManuscriptPropertyService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private static ManuscriptPropertyDefinition Def(
        string key, CustomPropertyType type = CustomPropertyType.String,
        ManuscriptPropertyScope scope = ManuscriptPropertyScope.Scene,
        List<string>? options = null)
        => new() { Key = key, Label = key, Type = type, Scope = scope, EnumOptions = options };

    [Fact]
    public async Task Definitions_StartEmpty_AndRoundTrip()
    {
        Assert.Empty(_sut.Definitions());

        // One of every type a manuscript object can hold, so a type that
        // silently degraded to text would show up here.
        var saved = await _sut.SetDefinitionsAsync([
            Def("tension", CustomPropertyType.Int),
            Def("note"),
            Def("done", CustomPropertyType.Bool),
            Def("due", CustomPropertyType.Date),
            Def("mood", CustomPropertyType.Enum, options: ["Calm", "Tense"])
        ]);

        Assert.Equal(
            [
                CustomPropertyType.Int, CustomPropertyType.String, CustomPropertyType.Bool,
                CustomPropertyType.Date, CustomPropertyType.Enum
            ],
            saved.Select(d => d.Type));
        Assert.Equal(5, _sut.Definitions(ManuscriptPropertyScope.Scene).Count);
        Assert.Empty(_sut.Definitions(ManuscriptPropertyScope.Chapter));
    }

    [Fact]
    public async Task SetDefinitions_DropsBlanksAndDuplicates_ButKeepsOneKeyPerScope()
    {
        var saved = await _sut.SetDefinitionsAsync([
            Def("tension"),
            new ManuscriptPropertyDefinition { Key = "  ", Label = "blank key" },
            new ManuscriptPropertyDefinition { Key = "mood", Label = "   " },
            Def("TENSION"),                                              // same key, same scope
            Def("tension", scope: ManuscriptPropertyScope.Chapter)       // same key, other scope
        ]);

        Assert.Equal(2, saved.Count);
        Assert.Equal(
            [ManuscriptPropertyScope.Scene, ManuscriptPropertyScope.Chapter],
            saved.Select(d => d.Scope));
    }

    [Fact]
    public async Task SetDefinitions_EnumWithoutOptions_FallsBackToText()
    {
        var saved = await _sut.SetDefinitionsAsync([
            Def("pass", CustomPropertyType.Enum),
            Def("mood", CustomPropertyType.Enum, options: ["Calm", "  ", "Tense", "calm"])
        ]);

        Assert.Equal(CustomPropertyType.String, saved[0].Type);
        Assert.Null(saved[0].EnumOptions);
        // Blanks dropped, case-insensitive duplicate dropped.
        Assert.Equal(["Calm", "Tense"], saved[1].EnumOptions);
    }

    [Fact]
    public async Task SetDefinitions_WithoutBook_Throws()
    {
        var empty = new ManuscriptPropertyService(new ProjectService(new FileService()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => empty.SetDefinitionsAsync([Def("x")]));
    }

    [Theory]
    [InlineData(CustomPropertyType.Int, "7", "7")]
    [InlineData(CustomPropertyType.Int, " 7 ", "7")]
    [InlineData(CustomPropertyType.Int, "high", null)]
    [InlineData(CustomPropertyType.Bool, "true", "true")]
    [InlineData(CustomPropertyType.Bool, "false", null)]
    [InlineData(CustomPropertyType.Bool, "yes", null)]
    [InlineData(CustomPropertyType.Date, "2026-03-04", "2026-03-04")]
    [InlineData(CustomPropertyType.Date, "not a date", null)]
    [InlineData(CustomPropertyType.String, "  a note  ", "a note")]
    [InlineData(CustomPropertyType.String, "   ", null)]
    public void Normalise_KeepsWhatTheTypeCanHold(
        CustomPropertyType type, string input, string? expected)
        => Assert.Equal(expected, ManuscriptPropertyService.Normalise(Def("k", type), input));

    [Fact]
    public void Normalise_Enum_MatchesAnOptionCaseInsensitively()
    {
        var definition = Def("mood", CustomPropertyType.Enum, options: ["Calm", "Tense"]);
        Assert.Equal("Tense", ManuscriptPropertyService.Normalise(definition, "tense"));
        Assert.Null(ManuscriptPropertyService.Normalise(definition, "furious"));
    }

    [Fact]
    public async Task SceneValues_SetReadAndClear()
    {
        await _sut.SetDefinitionsAsync([Def("tension", CustomPropertyType.Int)]);

        var values = await _sut.SetSceneValueAsync(_scene.Id, "tension", "8");
        Assert.Equal("8", values["tension"]);
        Assert.Equal("8", _sut.SceneValues(_scene.Id)["tension"]);

        // A blank clears rather than storing an empty string, so "not set" and
        // "set to nothing" stay one state.
        Assert.Empty(await _sut.SetSceneValueAsync(_scene.Id, "tension", ""));
        Assert.Empty(_sut.SceneValues(_scene.Id));
    }

    [Fact]
    public async Task ChapterValues_SetAndRead()
    {
        await _sut.SetDefinitionsAsync([
            Def("pov", scope: ManuscriptPropertyScope.Chapter)
        ]);

        var values = await _sut.SetChapterValueAsync(_chapter.Guid, "pov", "Mira");

        Assert.Equal("Mira", values["pov"]);
        Assert.Equal("Mira", _sut.ChapterValues(_chapter.Guid)["pov"]);
    }

    [Fact]
    public async Task SetValue_UnknownPropertyOrObject_Throws()
    {
        await _sut.SetDefinitionsAsync([Def("tension", CustomPropertyType.Int)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetSceneValueAsync(_scene.Id, "nope", "1"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetSceneValueAsync("no-such-scene", "tension", "1"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetChapterValueAsync("no-such-chapter", "tension", "1"));
        // A scene field cannot be set on a chapter: the scopes are separate
        // lists, and a chapter that has no such field has nowhere to put it.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetChapterValueAsync(_chapter.Guid, "tension", "1"));
    }

    [Fact]
    public async Task DeletingADefinition_TakesItsValuesWithIt()
    {
        await _sut.SetDefinitionsAsync([
            Def("tension", CustomPropertyType.Int),
            Def("mood"),
            Def("pov", scope: ManuscriptPropertyScope.Chapter)
        ]);
        await _sut.SetSceneValueAsync(_scene.Id, "tension", "8");
        await _sut.SetSceneValueAsync(_scene.Id, "mood", "grim");
        await _sut.SetChapterValueAsync(_chapter.Guid, "pov", "Mira");

        // Values left behind would be invisible everywhere yet still travel
        // with the project, and would resurface under any later field that
        // happened to reuse the key.
        await _sut.SetDefinitionsAsync([Def("mood")]);

        Assert.Equal(["mood"], _sut.SceneValues(_scene.Id).Keys);
        Assert.Empty(_sut.ChapterValues(_chapter.Guid));
    }

    [Fact]
    public async Task ArchivedScenesKeepTheirValues_AndArePruned()
    {
        await _sut.SetDefinitionsAsync([Def("mood")]);
        await _sut.SetSceneValueAsync(_scene.Id, "mood", "grim");
        await _projects.ArchiveSceneAsync(_chapter.Guid, _scene.Id);

        // Still readable while archived - the scene may come back.
        Assert.Equal("grim", _sut.SceneValues(_scene.Id)["mood"]);

        await _sut.SetDefinitionsAsync([]);
        Assert.Empty(_sut.SceneValues(_scene.Id));
    }
}
