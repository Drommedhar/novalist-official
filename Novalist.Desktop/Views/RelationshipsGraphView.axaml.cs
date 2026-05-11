using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class RelationshipsGraphView : UserControl
{
    private RelationshipsGraphViewModel? _vm;
    private Canvas? _canvas;
    private Border? _viewport;
    private ScaleTransform? _zoom;
    private TranslateTransform? _pan;

    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private bool _centerPending;

    public RelationshipsGraphView()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("GraphCanvas");
        _viewport = this.FindControl<Border>("ViewportHost");
        if (_canvas?.RenderTransform is TransformGroup tg)
        {
            foreach (var t in tg.Children)
            {
                if (t is ScaleTransform s) _zoom = s;
                else if (t is TranslateTransform tr) _pan = tr;
            }
        }

        DataContextChanged += OnDataContextChanged;
        if (_viewport != null)
        {
            _viewport.PointerPressed += OnViewportPointerPressed;
            _viewport.PointerMoved += OnViewportPointerMoved;
            _viewport.PointerReleased += OnViewportPointerReleased;
            _viewport.PointerWheelChanged += OnViewportPointerWheel;
            _viewport.LayoutUpdated += OnViewportLayoutUpdated;
        }
    }

    private void ScheduleCenter()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_centerPending) return;
            if (_viewport?.Bounds.Width > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[RelGraph] ScheduleCenter run. viewport={_viewport.Bounds.Width}x{_viewport.Bounds.Height}");
                _centerPending = false;
                CenterOnGraph();
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnViewportLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_centerPending) return;
        if (_viewport == null || _viewport.Bounds.Width <= 0 || _viewport.Bounds.Height <= 0) return;
        System.Diagnostics.Debug.WriteLine($"[RelGraph] LayoutUpdated trigger center. viewport={_viewport.Bounds.Width}x{_viewport.Bounds.Height}");
        _centerPending = false;
        CenterOnGraph();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.Nodes.CollectionChanged -= OnGraphChanged;
            _vm.Edges.CollectionChanged -= OnGraphChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as RelationshipsGraphViewModel;

        if (_vm != null)
        {
            _vm.Nodes.CollectionChanged += OnGraphChanged;
            _vm.Edges.CollectionChanged += OnGraphChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Rebuild();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RelationshipsGraphViewModel.Nodes)
                            or nameof(RelationshipsGraphViewModel.Edges))
            Rebuild();
    }

    private void OnGraphChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void CenterOnGraph()
    {
        if (_vm == null || _viewport == null || _pan == null || _zoom == null)
        {
            System.Diagnostics.Debug.WriteLine($"[RelGraph] CenterOnGraph abort. vmNull={_vm==null} viewportNull={_viewport==null} panNull={_pan==null} zoomNull={_zoom==null}");
            return;
        }
        if (_vm.Nodes.Count == 0 && _vm.GroupBoxes.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        const double nW = 80, nH = 28;
        foreach (var n in _vm.Nodes)
        {
            minX = Math.Min(minX, n.X);
            minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + nW);
            maxY = Math.Max(maxY, n.Y + nH);
        }
        foreach (var b in _vm.GroupBoxes)
        {
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.X + b.Width);
            maxY = Math.Max(maxY, b.Y + b.Height);
        }
        if (minX == double.MaxValue) return;

        var graphW = maxX - minX;
        var graphH = maxY - minY;
        var viewW = _viewport.Bounds.Width;
        var viewH = _viewport.Bounds.Height;
        if (viewW <= 0 || viewH <= 0) return;

        // Pick zoom: fit-with-margin, clamp.
        var scaleX = (viewW - 80) / graphW;
        var scaleY = (viewH - 80) / graphH;
        var scale = Math.Clamp(Math.Min(scaleX, scaleY), 0.2, 1.5);
        _zoom.ScaleX = _zoom.ScaleY = scale;

        var midX = (minX + maxX) / 2 * scale;
        var midY = (minY + maxY) / 2 * scale;
        var beforePan = (_pan.X, _pan.Y);
        var beforeScale = (_zoom.ScaleX, _zoom.ScaleY);
        _pan.X = viewW / 2 - midX;
        _pan.Y = viewH / 2 - midY;
        System.Diagnostics.Debug.WriteLine($"[RelGraph] CenterOnGraph applied. bbox=({minX:F0},{minY:F0})-({maxX:F0},{maxY:F0}) view={viewW:F0}x{viewH:F0} scale={scale:F2} pan=({_pan.X:F0},{_pan.Y:F0})");
        System.Diagnostics.Debug.WriteLine($"[RelGraph]  before pan={beforePan} scale={beforeScale} after pan=({_pan.X},{_pan.Y}) scale=({_zoom.ScaleX},{_zoom.ScaleY})");
        System.Diagnostics.Debug.WriteLine($"[RelGraph]  canvas RenderTransform = {_canvas?.RenderTransform?.Value}");
    }

    // ── Pan ──────────────────────────────────────────────────────

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pan == null || _viewport == null) return;
        var point = e.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed) return;
        _isPanning = true;
        _panStart = point.Position;
        _panStartX = _pan.X;
        _panStartY = _pan.Y;
        e.Pointer.Capture(_viewport);
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || _pan == null || _viewport == null) return;
        var cur = e.GetPosition(_viewport);
        _pan.X = _panStartX + (cur.X - _panStart.X);
        _pan.Y = _panStartY + (cur.Y - _panStart.Y);
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    // ── Zoom ─────────────────────────────────────────────────────

    private void OnViewportPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_zoom == null || _pan == null || _viewport == null) return;
        var oldScale = _zoom.ScaleX;
        var factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Math.Clamp(oldScale * factor, 0.2, 4.0);
        if (Math.Abs(newScale - oldScale) < 1e-6) return;

        // Zoom toward cursor: keep point under cursor fixed in viewport coords.
        var cursor = e.GetPosition(_viewport);
        var oldPanX = _pan.X;
        var oldPanY = _pan.Y;
        var localX = (cursor.X - oldPanX) / oldScale;
        var localY = (cursor.Y - oldPanY) / oldScale;
        _zoom.ScaleX = _zoom.ScaleY = newScale;
        _pan.X = cursor.X - localX * newScale;
        _pan.Y = cursor.Y - localY * newScale;
        System.Diagnostics.Debug.WriteLine($"[RelGraph] Wheel cursor=({cursor.X:F1},{cursor.Y:F1}) oldScale={oldScale:F3} newScale={newScale:F3} oldPan=({oldPanX:F1},{oldPanY:F1}) newPan=({_pan.X:F1},{_pan.Y:F1}) local=({localX:F1},{localY:F1})");
        e.Handled = true;
    }

    // ── Rebuild ──────────────────────────────────────────────────

    private void Rebuild()
    {
        if (_canvas == null || _vm == null) return;
        _canvas.Children.Clear();
        _centerPending = true;
        // Always re-center after the rebuild completes. If viewport size is
        // already known, run on Loaded priority; else LayoutUpdated catches it
        // when the viewport finally measures.
        ScheduleCenter();

        var stroke = (IBrush?)Application.Current?.FindResource("SubtleText") ?? Brushes.Gray;

        // Group boxes rendered first so edges + nodes draw on top. Distinct
        // color per box from a palette (cycles if more boxes than palette size).
        Color[] palette =
        {
            Color.FromRgb(137, 180, 250), // steel blue
            Color.FromRgb(166, 227, 161), // green
            Color.FromRgb(250, 179, 135), // orange
            Color.FromRgb(245, 194, 231), // pink
            Color.FromRgb(148, 226, 213), // teal
            Color.FromRgb(249, 226, 175), // gold
            Color.FromRgb(243, 139, 168), // rose
            Color.FromRgb(203, 166, 247), // purple
            Color.FromRgb(116, 199, 236), // sky
            Color.FromRgb(180, 190, 254), // lavender
        };
        for (int i = 0; i < _vm.GroupBoxes.Count; i++)
        {
            var box = _vm.GroupBoxes[i];
            var baseColor = palette[i % palette.Length];
            var fill = new SolidColorBrush(Color.FromArgb(34, baseColor.R, baseColor.G, baseColor.B));
            var border = new SolidColorBrush(baseColor);
            var rect = new Border
            {
                Width = box.Width,
                Height = box.Height,
                Background = fill,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Opacity = 0.9,
            };
            Canvas.SetLeft(rect, box.X);
            Canvas.SetTop(rect, box.Y);
            _canvas.Children.Add(rect);
            if (!string.IsNullOrWhiteSpace(box.Label))
            {
                var lbl = new TextBlock
                {
                    Text = box.Label,
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Opacity = 0.85,
                    Foreground = border,
                };
                Canvas.SetLeft(lbl, box.X + 8);
                Canvas.SetTop(lbl, box.Y + 4);
                _canvas.Children.Add(lbl);
            }
        }
        foreach (var edge in _vm.Edges)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(edge.X1, edge.Y1),
                EndPoint = new Point(edge.X2, edge.Y2),
                Stroke = stroke,
                StrokeThickness = 1,
                Opacity = 0.5,
            });
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var label = new TextBlock
                {
                    Text = edge.Label,
                    FontSize = 10,
                    Opacity = 0.65,
                    Foreground = stroke,
                };
                Canvas.SetLeft(label, edge.LabelX);
                Canvas.SetTop(label, edge.LabelY);
                _canvas.Children.Add(label);
            }
        }

        var nodeFill = (IBrush?)Application.Current?.FindResource("CardBackground") ?? Brushes.DarkSlateGray;
        var nodeBorder = (IBrush?)Application.Current?.FindResource("AccentBrush") ?? Brushes.SteelBlue;
        var nodeText = (IBrush?)Application.Current?.FindResource("NormalText") ?? Brushes.White;
        foreach (var node in _vm.Nodes)
        {
            var border = new Border
            {
                Width = 80,
                Padding = new Thickness(6, 4),
                Background = nodeFill,
                BorderBrush = nodeBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = new TextBlock
                {
                    Text = node.Name,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = nodeText,
                },
            };
            Canvas.SetLeft(border, node.X);
            Canvas.SetTop(border, node.Y);
            _canvas.Children.Add(border);
        }
    }
}
