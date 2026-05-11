using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class RelationshipsGraphViewModel : ObservableObject
{
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private ObservableCollection<RelationshipNode> _nodes = [];

    [ObservableProperty]
    private ObservableCollection<RelationshipEdge> _edges = [];

    [ObservableProperty]
    private bool _isLoading;

    public RelationshipsGraphViewModel(IEntityService entityService)
    {
        _entityService = entityService;
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var characters = await _entityService.LoadCharactersAsync();
            BuildGraph(characters.ToList());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildGraph(IReadOnlyList<CharacterData> characters)
    {
        Nodes.Clear();
        Edges.Clear();

        int count = characters.Count;
        if (count == 0) return;

        // Circular layout — radius scales with count.
        const double centerX = 480;
        const double centerY = 360;
        var radius = Math.Min(320, 100 + count * 18);
        var byName = new Dictionary<string, RelationshipNode>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++)
        {
            var c = characters[i];
            var angle = i * 2 * Math.PI / count - Math.PI / 2;
            var node = new RelationshipNode
            {
                Id = c.Id,
                Name = c.DisplayName,
                X = centerX + radius * Math.Cos(angle) - 40,
                Y = centerY + radius * Math.Sin(angle) - 14,
            };
            Nodes.Add(node);
            byName[c.DisplayName] = node;
            if (!string.IsNullOrWhiteSpace(c.Name)) byName[c.Name] = node;
        }

        foreach (var c in characters)
        {
            if (!byName.TryGetValue(c.DisplayName, out var fromNode)) continue;
            foreach (var rel in c.Relationships)
            {
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!byName.TryGetValue(target, out var toNode)) continue;
                    Edges.Add(new RelationshipEdge
                    {
                        X1 = fromNode.X + 40,
                        Y1 = fromNode.Y + 14,
                        X2 = toNode.X + 40,
                        Y2 = toNode.Y + 14,
                        Label = rel.Role,
                        LabelX = (fromNode.X + toNode.X) / 2 + 40,
                        LabelY = (fromNode.Y + toNode.Y) / 2 + 14,
                    });
                }
            }
        }
    }
}

public sealed class RelationshipNode
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed class RelationshipEdge
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Label { get; init; } = string.Empty;
    public double LabelX { get; init; }
    public double LabelY { get; init; }

    public Avalonia.Point StartPoint => new(X1, Y1);
    public Avalonia.Point EndPoint => new(X2, Y2);
}
