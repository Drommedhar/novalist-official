using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Novalist.Core.Models;

namespace Novalist.Desktop.Dialogs;

/// <summary>
/// Editor for user-authored road/river cross-section profiles. Operates on a
/// working copy; <see cref="Result"/> is the new profile list, or null on cancel.
/// </summary>
public partial class MapProfileEditorDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();
    public List<MapProfile>? Result { get; private set; }

    public ObservableCollection<ProfileRow> Profiles { get; } = new();

    public MapProfileEditorDialog(IEnumerable<MapProfile> existing)
    {
        InitializeComponent();
        foreach (var p in existing) Profiles.Add(ProfileRow.From(p));
        ProfileList.ItemsSource = Profiles;
        if (Profiles.Count > 0) ProfileList.SelectedIndex = 0;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Cancel();
    }

    private void OnAddProfile(object? sender, RoutedEventArgs e)
    {
        var row = new ProfileRow
        {
            Id = "profile-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = $"Profile {Profiles.Count + 1}",
            Kind = "road",
            DefaultWidth = 24,
            CasingColor = Color.Parse("#3f3413"),
            CasingExtra = 3,
        };
        row.Bands.Add(new BandRow { From = -1, To = 1, Color = Color.Parse("#cfcfcf") });
        Profiles.Add(row);
        ProfileList.SelectedItem = row;
    }

    private void OnDeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileRow row)
        {
            var idx = Profiles.IndexOf(row);
            Profiles.Remove(row);
            if (Profiles.Count > 0)
                ProfileList.SelectedIndex = Math.Min(idx, Profiles.Count - 1);
        }
    }

    private void OnAddBand(object? sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileRow row)
            row.Bands.Add(new BandRow { From = -1, To = 1, Color = Color.Parse("#cccccc") });
    }

    private void OnDeleteBand(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is BandRow band && ProfileList.SelectedItem is ProfileRow row)
            row.Bands.Remove(band);
    }

    private void OnAddMarking(object? sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileRow row)
            row.Markings.Add(new MarkingRow { Offset = 0, Color = Color.Parse("#ffffff"), Width = 1.6, Dash = string.Empty });
    }

    private void OnDeleteMarking(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is MarkingRow mk && ProfileList.SelectedItem is ProfileRow row)
            row.Markings.Remove(mk);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = Profiles.Select(r => r.ToModel()).ToList();
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        Result = null;
        DialogClosed.TrySetResult();
    }

    // ── Working-copy rows ───────────────────────────────────────────────
    public partial class ProfileRow : ObservableObject
    {
        public string Id { get; set; } = string.Empty;

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _kind = "road";
        [ObservableProperty] private double _defaultWidth = 24;
        [ObservableProperty] private Color _casingColor = Colors.Black;
        [ObservableProperty] private double _casingExtra = 3;

        public ObservableCollection<BandRow> Bands { get; } = new();
        public ObservableCollection<MarkingRow> Markings { get; } = new();

        public override string ToString() => Name;

        public static ProfileRow From(MapProfile p)
        {
            var r = new ProfileRow
            {
                Id = p.Id,
                Name = p.Name,
                Kind = string.IsNullOrEmpty(p.Kind) ? "road" : p.Kind,
                DefaultWidth = p.DefaultWidth,
                CasingColor = ParseColor(p.CasingColor, "#3f3413"),
                CasingExtra = p.CasingExtra,
            };
            foreach (var b in p.Bands)
                r.Bands.Add(new BandRow { From = b.From, To = b.To, Color = ParseColor(b.Color, "#cccccc") });
            foreach (var m in p.Markings)
                r.Markings.Add(new MarkingRow
                {
                    Offset = m.Offset,
                    Color = ParseColor(m.Color, "#ffffff"),
                    Width = m.Width,
                    Dash = m.Dash != null && m.Dash.Count > 0
                        ? string.Join(",", m.Dash.Select(d => d.ToString(CultureInfo.InvariantCulture)))
                        : string.Empty,
                });
            return r;
        }

        public MapProfile ToModel()
        {
            var m = new MapProfile
            {
                Id = string.IsNullOrEmpty(Id) ? "profile-" + Guid.NewGuid().ToString("N").Substring(0, 8) : Id,
                Name = string.IsNullOrWhiteSpace(Name) ? "Profile" : Name.Trim(),
                Kind = Kind,
                DefaultWidth = DefaultWidth,
                CasingColor = Hex(CasingColor),
                CasingExtra = CasingExtra,
            };
            foreach (var b in Bands)
                m.Bands.Add(new MapProfileBand { From = b.From, To = b.To, Color = Hex(b.Color) });
            foreach (var mk in Markings)
            {
                var dash = ParseDash(mk.Dash);
                m.Markings.Add(new MapProfileMarking
                {
                    Offset = mk.Offset,
                    Color = Hex(mk.Color),
                    Width = mk.Width,
                    Dash = dash,
                });
            }
            return m;
        }
    }

    public partial class BandRow : ObservableObject
    {
        [ObservableProperty] private double _from = -1;
        [ObservableProperty] private double _to = 1;
        [ObservableProperty] private Color _color = Colors.Gray;
    }

    public partial class MarkingRow : ObservableObject
    {
        [ObservableProperty] private double _offset;
        [ObservableProperty] private Color _color = Colors.White;
        [ObservableProperty] private double _width = 1.6;
        [ObservableProperty] private string _dash = string.Empty;
    }

    private static Color ParseColor(string? hex, string fallback)
        => Color.TryParse(hex, out var c) ? c : Color.Parse(fallback);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static List<double>? ParseDash(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
            if (double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                nums.Add(d);
        return nums.Count > 0 ? nums : null;
    }
}
