using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Invented languages and their dictionaries.</summary>
public sealed class ConlangRpc
{
    private readonly Workspace _workspace;
    private readonly ConlangService _service;

    public ConlangRpc(Workspace workspace)
    {
        _workspace = workspace;
        _service = new ConlangService(workspace.Projects);
    }

    [JsonRpcMethod("conlang/list")]
    public ConlangLanguageDto[] List()
        => [.. _service.GetAll().Select(ToDto)];

    [JsonRpcMethod("conlang/create")]
    public async Task<ConlangLanguageDto[]> CreateAsync(string name)
    {
        await _service.CreateAsync(name);
        return List();
    }

    [JsonRpcMethod("conlang/update")]
    public async Task<ConlangLanguageDto[]> UpdateAsync(
        string languageId, string? name = null, string? description = null)
    {
        await _service.UpdateAsync(languageId, name, description);
        return List();
    }

    /// <summary>
    /// Removes a language and every word in it. The one call here that loses
    /// anything, so the interface asks first.
    /// </summary>
    [JsonRpcMethod("conlang/delete")]
    public async Task<ConlangLanguageDto[]> DeleteAsync(string languageId)
    {
        await _service.DeleteAsync(languageId);
        return List();
    }

    /// <summary>Adds a word, or rewrites one. An empty id creates.</summary>
    [JsonRpcMethod("conlang/saveWord")]
    public async Task<ConlangLanguageDto[]> SaveWordAsync(
        string languageId, string? wordId, string word, string meaning,
        string? partOfSpeech = null, string? pronunciation = null, string? notes = null)
    {
        await _service.SaveWordAsync(languageId, new ConlangWord
        {
            Id = wordId ?? string.Empty,
            Word = word,
            Meaning = meaning,
            PartOfSpeech = partOfSpeech ?? string.Empty,
            Pronunciation = pronunciation ?? string.Empty,
            Notes = notes ?? string.Empty
        });
        return List();
    }

    [JsonRpcMethod("conlang/deleteWord")]
    public async Task<ConlangLanguageDto[]> DeleteWordAsync(string languageId, string wordId)
    {
        await _service.DeleteWordAsync(languageId, wordId);
        return List();
    }

    /// <summary>
    /// Words matching a query, by what they are or by what they mean.
    ///
    /// Both directions, because a writer mid-sentence either has the invented
    /// word and wants the meaning, or has the meaning and wants to know whether
    /// they already coined a word for it.
    /// </summary>
    [JsonRpcMethod("conlang/lookup")]
    public ConlangHitDto[] Lookup(string query, string? languageId = null)
        => [.. _service.Lookup(query, languageId)
            .Select(h => new ConlangHitDto(h.LanguageId, h.LanguageName, ToDto(h.Word)))];

    private static ConlangLanguageDto ToDto(ConlangLanguage language)
        => new(language.Id, language.Name, language.Description,
            [.. language.Words.Select(ToDto)]);

    private static ConlangWordDto ToDto(ConlangWord word)
        => new(word.Id, word.Word, word.Meaning, word.PartOfSpeech, word.Pronunciation, word.Notes);
}

/// <summary>One word of an invented language.</summary>
public sealed record ConlangWordDto(
    string Id, string Word, string Meaning, string PartOfSpeech,
    string Pronunciation, string Notes);

/// <summary>An invented language and its dictionary.</summary>
public sealed record ConlangLanguageDto(
    string Id, string Name, string Description, IReadOnlyList<ConlangWordDto> Words);

/// <summary>A word a lookup found, and which language it belongs to.</summary>
public sealed record ConlangHitDto(string LanguageId, string LanguageName, ConlangWordDto Word);
