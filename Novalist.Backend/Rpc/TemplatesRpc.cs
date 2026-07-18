using System.Text.Json;
using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Entity template management: list/get/save/delete per entity type, plus the
/// known-field catalog the template editor renders checkboxes for. Templates
/// live on the active book; custom-type templates share one list keyed by
/// EntityTypeKey.
/// </summary>
public sealed class TemplatesRpc(Workspace workspace)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private BookData Book => workspace.Projects.ActiveBook
        ?? throw new InvalidOperationException("No project open.");

    [JsonRpcMethod("templates/list")]
    public TemplateSummaryDto[] List(string type)
    {
        var book = Book;
        return type switch
        {
            "character" => book.CharacterTemplates.Select(t => new TemplateSummaryDto(t.Id, t.Name, t.BuiltIn)).ToArray(),
            "location" => book.LocationTemplates.Select(t => new TemplateSummaryDto(t.Id, t.Name, t.BuiltIn)).ToArray(),
            "item" => book.ItemTemplates.Select(t => new TemplateSummaryDto(t.Id, t.Name, t.BuiltIn)).ToArray(),
            "lore" => book.LoreTemplates.Select(t => new TemplateSummaryDto(t.Id, t.Name, t.BuiltIn)).ToArray(),
            _ => book.CustomEntityTemplates
                .Where(t => t.EntityTypeKey == type)
                .Select(t => new TemplateSummaryDto(t.Id, t.Name, t.BuiltIn))
                .ToArray()
        };
    }

    [JsonRpcMethod("templates/knownFields")]
    public string[] KnownFields(string type) => type switch
    {
        "character" => TemplateKnownFields.Character,
        "location" => TemplateKnownFields.Location,
        "item" => TemplateKnownFields.Item,
        "lore" => TemplateKnownFields.Lore,
        _ => workspace.Projects.CurrentProject?.CustomEntityTypes
                .FirstOrDefault(t => t.TypeKey == type)?
                .DefaultFields.Select(f => f.Key).ToArray()
            ?? []
    };

    [JsonRpcMethod("templates/get")]
    public JsonElement Get(string type, string id)
    {
        object template = (type switch
        {
            "character" => Book.CharacterTemplates.FirstOrDefault(t => t.Id == id) as object,
            "location" => Book.LocationTemplates.FirstOrDefault(t => t.Id == id),
            "item" => Book.ItemTemplates.FirstOrDefault(t => t.Id == id),
            "lore" => Book.LoreTemplates.FirstOrDefault(t => t.Id == id),
            _ => Book.CustomEntityTemplates.FirstOrDefault(t => t.Id == id && t.EntityTypeKey == type)
        }) ?? throw new InvalidOperationException($"Unknown template: {id}");
        return JsonSerializer.SerializeToElement(template, JsonOptions);
    }

    [JsonRpcMethod("templates/save")]
    public async Task<JsonElement> SaveAsync(string type, JsonElement template)
    {
        var book = Book;
        object saved;
        switch (type)
        {
            case "character":
                saved = Upsert(book.CharacterTemplates, Materialize<CharacterTemplate>(template));
                break;
            case "location":
                saved = Upsert(book.LocationTemplates, Materialize<LocationTemplate>(template));
                break;
            case "item":
                saved = Upsert(book.ItemTemplates, Materialize<ItemTemplate>(template));
                break;
            case "lore":
                saved = Upsert(book.LoreTemplates, Materialize<LoreTemplate>(template));
                break;
            default:
                var custom = Materialize<CustomEntityTemplate>(template);
                custom.EntityTypeKey = type;
                saved = Upsert(book.CustomEntityTemplates, custom);
                break;
        }
        await workspace.Projects.SaveProjectAsync();
        return JsonSerializer.SerializeToElement(saved, JsonOptions);
    }

    [JsonRpcMethod("templates/delete")]
    public async Task DeleteAsync(string type, string id)
    {
        var book = Book;
        switch (type)
        {
            case "character":
                book.CharacterTemplates.RemoveAll(t => t.Id == id);
                if (book.ActiveCharacterTemplateId == id) book.ActiveCharacterTemplateId = string.Empty;
                break;
            case "location":
                book.LocationTemplates.RemoveAll(t => t.Id == id);
                if (book.ActiveLocationTemplateId == id) book.ActiveLocationTemplateId = string.Empty;
                break;
            case "item":
                book.ItemTemplates.RemoveAll(t => t.Id == id);
                if (book.ActiveItemTemplateId == id) book.ActiveItemTemplateId = string.Empty;
                break;
            case "lore":
                book.LoreTemplates.RemoveAll(t => t.Id == id);
                if (book.ActiveLoreTemplateId == id) book.ActiveLoreTemplateId = string.Empty;
                break;
            default:
                book.CustomEntityTemplates.RemoveAll(t => t.Id == id && t.EntityTypeKey == type);
                if (book.ActiveCustomEntityTemplateIds.TryGetValue(type, out var active) && active == id)
                    book.ActiveCustomEntityTemplateIds.Remove(type);
                break;
        }
        await workspace.Projects.SaveProjectAsync();
    }

    private static T Materialize<T>(JsonElement template) where T : class
    {
        var result = template.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException("Invalid template payload.");
        var idProp = typeof(T).GetProperty("Id")!;
        if (string.IsNullOrEmpty((string?)idProp.GetValue(result)))
            idProp.SetValue(result, Guid.NewGuid().ToString());
        return result;
    }

    private static T Upsert<T>(List<T> list, T template) where T : class
    {
        var idProp = typeof(T).GetProperty("Id")!;
        var id = (string)idProp.GetValue(template)!;
        var index = list.FindIndex(t => (string)idProp.GetValue(t)! == id);
        if (index >= 0) list[index] = template;
        else list.Add(template);
        return template;
    }
}

public sealed record TemplateSummaryDto(string Id, string Name, bool BuiltIn);
