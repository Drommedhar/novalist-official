using Microsoft.Maui.Storage;
using Novalist.Mobile.Services;

namespace Novalist.Mobile.Pages;

/// <summary>
/// Phase 0, checkpoint 1: prove the shared C# backend runs in-process on-device.
/// On appearing it round-trips system/ping and shows the result, then offers the
/// editor spike (checkpoint 2).
/// </summary>
public sealed class SeamPage : ContentPage
{
    private readonly Label _status = new()
    {
        Text = "Probing backend seam...",
        HorizontalTextAlignment = TextAlignment.Center,
        FontSize = 18,
    };

    private bool _probed;

    public SeamPage()
    {
        Title = "Novalist - Phase 0 seam";

        var editorButton = new Button { Text = "Open editor spike", IsEnabled = false };
        editorButton.Clicked += async (_, _) =>
            await Navigation.PushAsync(new EditorSpikePage());

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 20,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Novalist mobile seam probe",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                _status,
                editorButton,
            },
        };

        _editorButton = editorButton;
    }

    private readonly Button _editorButton;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_probed) return;
        _probed = true;

        var result = await SeamProbe.PingAsync(FileSystem.Current.AppDataDirectory);
        _status.Text = result.Ok
            ? $"Seam OK - {result.Detail}"
            : $"Seam FAILED - {result.Detail}";
        _status.TextColor = result.Ok ? Colors.MediumSeaGreen : Colors.IndianRed;
        _editorButton.IsEnabled = true;
    }
}
