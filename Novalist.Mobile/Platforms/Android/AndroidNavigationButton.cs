using Microsoft.Maui.Controls.Shapes;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private sealed class AndroidNavigationButton : Border
    {
        private readonly Button _button;
        private readonly Label _label;
        private readonly MauiPath _icon;
        private readonly Grid _visuals;
        private readonly bool _tab;
        private Color _foreground = Colors.White;
        private bool _selected;
        private bool _pressed;

        public AndroidNavigationButton(string title, string path, bool tab, string automationId)
        {
            _tab = tab;
            StrokeThickness = 0;
            StrokeShape = new RoundRectangle { CornerRadius = 10 };
            MinimumHeightRequest = 48;
            _icon = new MauiPath
            {
                Data = new PathGeometryConverter().ConvertFromInvariantString(path) as Geometry
                    ?? throw new InvalidOperationException("Navigation icon geometry could not be parsed."),
                WidthRequest = 22, HeightRequest = 22, Aspect = Stretch.Uniform,
                StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            _label = new Label
            {
                FontSize = tab ? 11 : 16, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = tab ? TextAlignment.Center : TextAlignment.Start
            };
            _visuals = CreateVisuals(tab);
            _button = new Button
            {
                AutomationId = automationId, BackgroundColor = Colors.Transparent, TextColor = Colors.Transparent,
                MinimumWidthRequest = 0, MinimumHeightRequest = 48, Padding = 0, CornerRadius = 10
            };
            _button.HandlerChanged += (_, _) => UpdateNativeSelection();
            _button.Pressed += (_, _) => { _pressed = true; UpdateAppearance(); };
            _button.Released += (_, _) => { _pressed = false; UpdateAppearance(); };
            _button.Focused += (_, _) => UpdateAppearance();
            _button.Unfocused += (_, _) => UpdateAppearance();
            var content = new Grid();
            content.Add(_visuals);
            content.Add(_button);
            Content = content;
            Title = title;
            SetForeground(_foreground);
        }

        public event EventHandler Clicked
        {
            add => _button.Clicked += value;
            remove => _button.Clicked -= value;
        }

        public string Title
        {
            set
            {
                _label.Text = value;
                SemanticProperties.SetDescription(_button, value);
            }
        }

        public bool Selected
        {
            set { _selected = value; UpdateAppearance(); UpdateNativeSelection(); }
        }

        public bool Compact
        {
            set
            {
                if (_tab) return;
                _label.IsVisible = !value;
                _visuals.Padding = value ? 0 : new Thickness(12, 0);
                _visuals.ColumnSpacing = value ? 0 : 12;
                _visuals.ColumnDefinitions[0].Width = value ? GridLength.Star : new GridLength(22);
                _visuals.ColumnDefinitions[1].Width = value ? new GridLength(0) : GridLength.Star;
            }
        }

        public void SetForeground(Color foreground)
        {
            _foreground = foreground;
            _icon.Stroke = foreground;
            _label.TextColor = foreground;
            UpdateAppearance();
        }

        private Grid CreateVisuals(bool tab)
        {
            var visuals = new Grid { InputTransparent = true, CascadeInputTransparent = true };
            AutomationProperties.SetIsInAccessibleTree(_icon, false);
            AutomationProperties.SetIsInAccessibleTree(_label, false);
            if (tab)
            {
                visuals.RowDefinitions = [new RowDefinition(24), new RowDefinition(GridLength.Auto)];
                visuals.RowSpacing = 2;
                visuals.VerticalOptions = LayoutOptions.Center;
                visuals.Add(_icon);
                visuals.Add(_label, 0, 1);
            }
            else
            {
                visuals.ColumnDefinitions = [new ColumnDefinition(22), new ColumnDefinition(GridLength.Star)];
                visuals.ColumnSpacing = 12;
                visuals.Padding = new Thickness(12, 0);
                visuals.Add(_icon);
                visuals.Add(_label, 1, 0);
            }
            return visuals;
        }

        private void UpdateAppearance()
        {
            BackgroundColor = _foreground.WithAlpha(_pressed ? 0.24f : _selected ? 0.16f : _button.IsFocused ? 0.12f : 0);
            _label.FontAttributes = _selected ? FontAttributes.Bold : FontAttributes.None;
        }

        private void UpdateNativeSelection()
        {
            if (_button.Handler?.PlatformView is Android.Views.View native) native.Selected = _selected;
        }
    }
}
