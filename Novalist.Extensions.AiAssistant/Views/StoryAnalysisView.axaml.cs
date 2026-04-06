using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Novalist.Extensions.AiAssistant.ViewModels;

namespace Novalist.Extensions.AiAssistant.Views;

public partial class StoryAnalysisView : UserControl
{
    public StoryAnalysisView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is StoryAnalysisViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StoryAnalysisViewModel.StreamingLog))
            ScrollToBottom(RawOutputScroll);
        else if (e.PropertyName == nameof(StoryAnalysisViewModel.StreamingThinking))
            ScrollToBottom(ThinkingScroll);
    }

    private static void ScrollToBottom(ScrollViewer? sv)
    {
        if (sv is null) return;
        sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is StoryAnalysisViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }
}
