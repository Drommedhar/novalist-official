using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class EntityImage : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _path = string.Empty;

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    [JsonPropertyName("path")]
    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class EntitySection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class EntityRelationship
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;
}

public enum EntityType
{
    Character,
    Location,
    Item,
    Lore
}

/// <summary>
/// Implemented by all entity data types to support World Bible tracking.
/// </summary>
public interface IEntityData
{
    string Id { get; }
    bool IsWorldBible { get; set; }
}
