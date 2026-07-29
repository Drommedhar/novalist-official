using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>How each entry type's sheet is laid out in this project.</summary>
public sealed class EntitySheetRpc
{
    private readonly Workspace _workspace;

    public EntitySheetRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private ProjectMetadata Project => _workspace.Projects.CurrentProject
        ?? throw new InvalidOperationException("No project open.");

    /// <summary>
    /// The layout for one entry type. A type nobody has arranged reports empty
    /// lists, which the sheet reads as "everything, in its natural order".
    /// </summary>
    [JsonRpcMethod("sheets/get")]
    public EntitySheetDto Get(string typeKey)
    {
        var sheet = Project.EntitySheets.FirstOrDefault(
            s => string.Equals(s.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));
        return new EntitySheetDto(
            typeKey,
            [.. sheet?.Hidden ?? []],
            [.. sheet?.Order ?? []]);
    }

    /// <summary>
    /// Replaces the layout. Hiding a field never touches its value: the field
    /// is out of the way, not gone, and showing it again brings back whatever
    /// was written there.
    /// </summary>
    [JsonRpcMethod("sheets/save")]
    public async Task<EntitySheetDto> SaveAsync(string typeKey, string[]? hidden, string[]? order)
    {
        var sheet = Project.EntitySheets.FirstOrDefault(
            s => string.Equals(s.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
        {
            sheet = new EntitySheet { TypeKey = typeKey };
            Project.EntitySheets.Add(sheet);
        }

        sheet.Hidden = [.. (hidden ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal)];
        sheet.Order = [.. (order ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal)];

        await _workspace.Projects.SaveProjectAsync();
        return Get(typeKey);
    }
}

/// <summary>
/// One entry type's sheet layout. Empty lists mean the default: every field,
/// in the order Novalist ships them in.
/// </summary>
public sealed record EntitySheetDto(string TypeKey, string[] Hidden, string[] Order);
