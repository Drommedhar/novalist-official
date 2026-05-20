using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Services.Wizards;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

public class WizardMappersTests
{
    private static WizardResult Result(params (string key, string text)[] answers)
    {
        var r = new WizardResult();
        foreach (var (k, t) in answers)
            r.Answers[k] = new WizardAnswer { Text = t };
        return r;
    }

    // ── EntityWizardMapper ──

    [Fact]
    public void BuildCharacter_MapsFields_AndDescriptionSection()
    {
        var r = Result(("name", " Alice "), ("surname", "Smith"), ("gender", "F"),
            ("age", "30"), ("role", "Hero"), ("group", "Guild"), ("description", "Brave"));
        var c = EntityWizardMapper.BuildCharacter(r);
        Assert.Equal("Alice", c.Name);
        Assert.Equal("Smith", c.Surname);
        Assert.Equal("Hero", c.Role);
        Assert.Contains(c.Sections, s => s.Title == "Description" && s.Content == "Brave");
    }

    [Fact]
    public void BuildCharacter_NoDescription_NoSection()
        => Assert.Empty(EntityWizardMapper.BuildCharacter(Result(("name", "A"))).Sections);

    [Fact]
    public void BuildLocation_Item_Lore()
    {
        var loc = EntityWizardMapper.BuildLocation(Result(("name", "City"), ("type", "Urban"), ("parent", "Region"), ("description", "Big")));
        Assert.Equal("City", loc.Name);
        Assert.Equal("Region", loc.Parent);

        var item = EntityWizardMapper.BuildItem(Result(("name", "Sword"), ("type", "Weapon"), ("origin", "Forge"), ("description", "Sharp")));
        Assert.Equal("Forge", item.Origin);

        var lore = EntityWizardMapper.BuildLore(Result(("name", "Magic"), ("category", "System"), ("description", "d")));
        Assert.Equal("System", lore.Category);
    }

    [Fact]
    public void BuildLore_NoCategory_DefaultsToOther()
        => Assert.Equal("Other", EntityWizardMapper.BuildLore(Result(("name", "X"))).Category);

    [Fact]
    public void BuildCustomEntity_PopulatesFieldsFromAnswers()
    {
        var def = new CustomEntityTypeDefinition
        {
            TypeKey = "faction",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "motto", Type = CustomPropertyType.String },
                new CustomEntityFieldDefinition { Key = "size", Type = CustomPropertyType.Int },
                new CustomEntityFieldDefinition { Key = "skipped", Type = CustomPropertyType.String }
            }
        };
        var r = new WizardResult();
        r.Answers["name"] = new WizardAnswer { Text = "Order" };
        r.Answers["motto"] = new WizardAnswer { Text = "Honor" };
        r.Answers["size"] = new WizardAnswer { Number = 42 };
        // "skipped" has no answer -> not added.

        var e = EntityWizardMapper.BuildCustomEntity(r, def);
        Assert.Equal("faction", e.EntityTypeKey);
        Assert.Equal("Order", e.Name);
        Assert.Equal("Honor", e.Fields["motto"]);
        Assert.Equal("42", e.Fields["size"]);
        Assert.False(e.Fields.ContainsKey("skipped"));
    }

    // ── CharacterInterviewMapper ──

    [Fact]
    public void Interview_AddsSections_ReplacesExisting_OverwritesName()
    {
        var character = new CharacterData { Name = "Old" };
        character.Sections.Add(new EntitySection { Title = "Wound", Content = "stale" });
        var r = Result(("name", "New"), ("wound", "betrayal"), ("fear", "loss"), ("voice", ""));

        CharacterInterviewMapper.Apply(character, r, overwriteName: true);

        Assert.Equal("New", character.Name);
        Assert.Equal("betrayal", character.Sections.First(s => s.Title == "Wound").Content); // replaced
        Assert.Contains(character.Sections, s => s.Title == "Fear" && s.Content == "loss");   // appended
        Assert.DoesNotContain(character.Sections, s => s.Title == "Voice");                    // blank skipped
    }

    [Fact]
    public void Interview_NoOverwriteName_KeepsName()
    {
        var c = new CharacterData { Name = "Keep" };
        CharacterInterviewMapper.Apply(c, Result(("name", "Ignored"), ("wound", "w")), overwriteName: false);
        Assert.Equal("Keep", c.Name);
    }

    // ── ProjectWizardMapper ──

    [Fact]
    public void ExtractProjectName_AndBookName_Fallbacks()
    {
        Assert.Equal("MyProj", ProjectWizardMapper.ExtractProjectName(Result(("projectName", "MyProj"))));
        Assert.Equal("MyBook", ProjectWizardMapper.ExtractBookName(Result(("bookName", "MyBook"))));
        Assert.Equal("MyProj", ProjectWizardMapper.ExtractBookName(Result(("projectName", "MyProj")))); // book falls back to project
        Assert.Equal("Book 1", ProjectWizardMapper.ExtractBookName(new WizardResult()));                // ultimate fallback
    }

    [Fact]
    public async Task ApplyAsync_NoActiveBook_NoOp()
    {
        var project = Substitute.For<IProjectService>();
        project.ActiveBook.Returns((BookData?)null);
        await ProjectWizardMapper.ApplyAsync(project, Substitute.For<IEntityService>(), new WizardResult());
        await project.DidNotReceiveWithAnyArgs().CreateChapterAsync(default!);
    }

    [Fact]
    public async Task ApplyAsync_SeedsActsChaptersCharactersAndSynopsis()
    {
        var project = Substitute.For<IProjectService>();
        var entity = Substitute.For<IEntityService>();
        var book = new BookData();
        project.ActiveBook.Returns(book);
        var chapters = new List<ChapterData>();
        project.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => { var c = new ChapterData { Title = ci.ArgAt<string>(0) }; chapters.Add(c); book.Chapters.Add(c); return c; });
        project.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());
        project.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new SceneData { Title = ci.ArgAt<string>(1) });

        var r = new WizardResult();
        r.Answers["chaptersPerAct"] = new WizardAnswer { Number = 2 };
        r.Answers["premise"] = new WizardAnswer { Text = "A premise" };
        r.Answers["actOne"] = new WizardAnswer { Text = "setup" };
        r.Answers["actTwo"] = new WizardAnswer { Text = "mid" };
        r.Answers["actThree"] = new WizardAnswer { Text = "end" };
        r.Answers["paragraph"] = new WizardAnswer { Text = "para" };
        r.Answers["protagonists"] = new WizardAnswer
        {
            List = new() { new() { ["name"] = new WizardAnswer { Text = "Alice" }, ["role"] = new WizardAnswer { Text = "Lead" } },
                           new() { ["name"] = new WizardAnswer { Text = "" } } } // blank name skipped
        };
        r.Answers["antagonists"] = new WizardAnswer
        {
            List = new() { new() { ["name"] = new WizardAnswer { Text = "Bob" } } } // role falls back to "Antagonist"
        };

        await ProjectWizardMapper.ApplyAsync(project, entity, r);

        Assert.Equal(3, book.Acts.Count);
        await project.Received(6).CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>()); // 3 acts * 2
        await entity.Received(2).SaveCharacterAsync(Arg.Any<CharacterData>());              // Alice + Bob
        await project.Received(1).SaveScenesAsync();
    }

    [Fact]
    public async Task ApplyAsync_ExistingFirstScene_ReusesIt()
    {
        var project = Substitute.For<IProjectService>();
        var book = new BookData();
        project.ActiveBook.Returns(book);
        var existingScene = new SceneData { Title = "Existing" };
        project.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => { var c = new ChapterData(); book.Chapters.Add(c); return c; });
        project.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData> { existingScene });

        await ProjectWizardMapper.ApplyAsync(project, Substitute.For<IEntityService>(), new WizardResult());

        // Synopsis stashed onto the existing scene; no new scene created.
        await project.DidNotReceiveWithAnyArgs().CreateSceneAsync(default!, default!);
    }
}
