using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class TemplatesRpcTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private Workspace _workspace = null!;
    private TemplatesRpc _rpc = null!;
    private EntitiesRpc _entities = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = new Workspace();
        await _workspace.Projects.CreateProjectAsync(_dir.Path, "TemplateProj", "Book One");
        _rpc = new TemplatesRpc(_workspace);
        _entities = new EntitiesRpc(_workspace);
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();
        return ValueTask.CompletedTask;
    }

    private static JsonElement Spec(object spec) => JsonSerializer.SerializeToElement(spec);

    [Fact]
    public void List_RequiresOpenProject()
    {
        var closed = new TemplatesRpc(new Workspace());
        Assert.Throws<InvalidOperationException>(() => closed.List("character"));
    }

    [Fact]
    public async Task Save_NullPayloadThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _rpc.SaveAsync("character", JsonSerializer.SerializeToElement((object?)null)));
    }

    [Fact]
    public async Task Save_NullIdGetsGenerated()
    {
        var saved = await _rpc.SaveAsync("character", Spec(new { id = (string?)null, name = "NoId" }));
        Assert.False(string.IsNullOrEmpty(saved.GetProperty("id").GetString()));
    }

    [Fact]
    public void KnownFields_PerType()
    {
        Assert.Contains("EyeColor", _rpc.KnownFields("character"));
        Assert.Equal(["Type", "Description"], _rpc.KnownFields("location"));
        Assert.Equal(["Type", "Description", "Origin"], _rpc.KnownFields("item"));
        Assert.Equal(["Category", "Description"], _rpc.KnownFields("lore"));
        Assert.Empty(_rpc.KnownFields("faction"));
    }

    [Fact]
    public async Task KnownFields_CustomTypeUsesDefinitionFields()
    {
        await new Novalist.Core.Services.EntityService(_workspace.Projects)
            .SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
            {
                TypeKey = "faction",
                DisplayName = "Faction",
                DisplayNamePlural = "Factions",
                DefaultFields = [new CustomEntityFieldDefinition { Key = "Motto" }]
            });
        Assert.Equal(["Motto"], _rpc.KnownFields("faction"));
    }

    [Fact]
    public async Task BuiltInTypes_FullTemplateCrud()
    {
        var saved = await _rpc.SaveAsync("character", Spec(new
        {
            name = "Hero",
            fields = new[] { new { key = "Gender", defaultValue = "" }, new { key = "Age", defaultValue = "30" } },
            customPropertyDefs = new[]
            {
                new { key = "Rank", type = "Enum", defaultValue = "Low", enumOptions = new[] { "Low", "High" } }
            },
            sections = new[] { new { title = "Wound", defaultContent = "tbd" } },
            includeRelationships = true,
            includeImages = false,
            includeChapterOverrides = true,
            ageMode = "date",
            ageIntervalUnit = "Months"
        }));
        var id = saved.GetProperty("id").GetString()!;
        Assert.False(string.IsNullOrEmpty(id));

        var list = _rpc.List("character");
        Assert.Single(list, t => t.Name == "Hero" && !t.BuiltIn);

        var fetched = _rpc.Get("character", id);
        Assert.Equal("date", fetched.GetProperty("ageMode").GetString());
        Assert.Equal("Months", fetched.GetProperty("ageIntervalUnit").GetString());
        Assert.False(fetched.GetProperty("includeImages").GetBoolean());

        var renamed = await _rpc.SaveAsync("character", Spec(new { id, name = "Heroine" }));
        Assert.Equal("Heroine", renamed.GetProperty("name").GetString());
        Assert.Single(_rpc.List("character"));

        Assert.Throws<InvalidOperationException>(() => _rpc.Get("character", "missing"));

        _workspace.Projects.ActiveBook!.ActiveCharacterTemplateId = id;
        await _rpc.DeleteAsync("character", id);
        Assert.Empty(_rpc.List("character"));
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.ActiveCharacterTemplateId);
    }

    [Fact]
    public async Task OtherBuiltInTypes_SaveListGetDelete()
    {
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var saved = await _rpc.SaveAsync(type, Spec(new { name = $"T-{type}" }));
            var id = saved.GetProperty("id").GetString()!;
            Assert.Single(_rpc.List(type), t => t.Name == $"T-{type}");
            Assert.Equal($"T-{type}", _rpc.Get(type, id).GetProperty("name").GetString());
            switch (type)
            {
                case "location": _workspace.Projects.ActiveBook!.ActiveLocationTemplateId = id; break;
                case "item": _workspace.Projects.ActiveBook!.ActiveItemTemplateId = id; break;
                default: _workspace.Projects.ActiveBook!.ActiveLoreTemplateId = id; break;
            }
            await _rpc.DeleteAsync(type, id);
            Assert.Empty(_rpc.List(type));
        }
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.ActiveLocationTemplateId);
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.ActiveItemTemplateId);
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.ActiveLoreTemplateId);
    }

    [Fact]
    public async Task CustomType_TemplateCrudAndCreateApplication()
    {
        await new Novalist.Core.Services.EntityService(_workspace.Projects)
            .SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
            {
                TypeKey = "faction",
                DisplayName = "Faction",
                DisplayNamePlural = "Factions"
            });

        var saved = await _rpc.SaveAsync("faction", Spec(new
        {
            name = "War Guild",
            fields = new[]
            {
                new { key = "Motto", defaultValue = "Strength" },
                new { key = "Banner", defaultValue = "" }
            },
            customPropertyDefs = new[] { new { key = "Size", type = "Int", defaultValue = "10" } },
            sections = new[] { new { title = "History", defaultContent = "Old." } }
        }));
        var templateId = saved.GetProperty("id").GetString()!;
        Assert.Equal("faction", saved.GetProperty("entityTypeKey").GetString());
        Assert.Single(_rpc.List("faction"));
        Assert.Equal("War Guild", _rpc.Get("faction", templateId).GetProperty("name").GetString());

        // The codex create dialog lists custom-type templates too.
        Assert.Single(_entities.GetTemplates("faction"), t => t.Name == "War Guild");

        // Creating a custom entity from the template applies fields, props, sections.
        var entity = await _entities.CreateAsync("faction", "Nordwacht", templateId);
        Assert.Equal(templateId, entity.GetProperty("templateId").GetString());
        Assert.Equal("Strength", entity.GetProperty("fields").GetProperty("Motto").GetString());
        Assert.Equal("10", entity.GetProperty("customProperties").GetProperty("Size").GetString());
        Assert.Equal("History", entity.GetProperty("sections")[0].GetProperty("title").GetString());

        // Creating with an unknown template id leaves the entity untouched.
        var plain = await _entities.CreateAsync("faction", "Plain", "no-such-template");
        Assert.False(
            plain.TryGetProperty("templateId", out var tid) && !string.IsNullOrEmpty(tid.GetString()));

        _workspace.Projects.ActiveBook!.ActiveCustomEntityTemplateIds["faction"] = templateId;
        await _rpc.DeleteAsync("faction", templateId);
        Assert.Empty(_rpc.List("faction"));
        Assert.False(_workspace.Projects.ActiveBook!.ActiveCustomEntityTemplateIds.ContainsKey("faction"));
    }
}
