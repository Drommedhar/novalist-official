using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example.ViewModels;

public partial class WordFrequencyViewModel : ObservableObject
{
    private readonly WordFrequencyService _service;
    private readonly IHostServices _host;

    public IExtensionLocalization Loc { get; }

    [ObservableProperty]
    private ObservableCollection<WordFrequencyEntry> _entries = [];

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public WordFrequencyViewModel(WordFrequencyService service, IHostServices host, IExtensionLocalization loc)
    {
        _service = service;
        _host = host;
        Loc = loc;
        _statusText = loc.T("wordFrequencyView.statusReady");
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsAnalyzing = true;
        StatusText = Loc.T("wordFrequencyView.statusAnalyzing");

        try
        {
            var allText = new StringBuilder();
            var chapters = _host.ProjectService.GetChaptersOrdered();
            foreach (var chapter in chapters)
            {
                var scenes = _host.ProjectService.GetScenesForChapter(chapter.Guid);
                foreach (var scene in scenes)
                {
                    var content = await _host.ProjectService.ReadSceneContentAsync(chapter.Guid, scene.Id);
                    allText.AppendLine(content);
                }
            }

            var results = _service.Analyze(allText.ToString());
            Entries = new ObservableCollection<WordFrequencyEntry>(results);
            StatusText = Loc.T("wordFrequencyView.statusDone", results.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("wordFrequencyView.statusError", ex.Message);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }
}
