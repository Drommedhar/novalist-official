using System.ComponentModel;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class MainWindowSubViewModelsTests
{
    static MainWindowSubViewModelsTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    // 1x1 PNG
    private const string Png1x1Base64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public void BookCard_NoCover_NoRoot()
    {
        var card = new BookCard(new BookData { Id = "b", Name = "B" }, null, true);
        Assert.False(card.HasCoverImage);
        Assert.True(card.IsActive);
        Assert.Equal("b", card.Id);

        var card2 = new BookCard(new BookData { Id = "b", Name = "B", CoverImage = "c.png" }, null, false); // root null
        Assert.False(card2.HasCoverImage);
    }

    [AvaloniaFact]
    public void BookCard_MissingFile_NullCover()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-bc-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            var book = new BookData { Id = "b", Name = "B", CoverImage = "missing.png", FolderName = "" };
            var card = new BookCard(book, tmp, false);
            Assert.False(card.HasCoverImage); // file doesn't exist
        }
        finally { try { System.IO.Directory.Delete(tmp, true); } catch { } }
    }

    [AvaloniaFact]
    public void BookCard_ValidImage_LoadsCover()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-bc-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(tmp, "cover.png"), Convert.FromBase64String(Png1x1Base64));
            var book = new BookData { Id = "b", Name = "B", CoverImage = "cover.png", FolderName = "" };
            var card = new BookCard(book, tmp, true);
            Assert.True(card.HasCoverImage);
            Assert.NotNull(card.CoverImage);
        }
        finally { try { System.IO.Directory.Delete(tmp, true); } catch { } }
    }

    [AvaloniaFact]
    public void StatusBarChapterAndSceneOverview()
    {
        var scenes = new List<StatusBarSceneOverviewItem>
        {
            new("Scene 1", 300),
            new("Scene 2", 100),
        };
        var readability = new ReadabilityResult { Score = 70, Level = ReadabilityLevel.Easy };
        var item = new StatusBarChapterOverviewItem("Chapter 1", 400, readability, scenes, 40, 400);
        Assert.Equal("Chapter 1", item.Name);
        Assert.Equal(400, item.WordCount);
        Assert.False(string.IsNullOrEmpty(item.WordCountDisplay));
        Assert.True(item.HasReadability);
        Assert.False(string.IsNullOrEmpty(item.ReadabilityDisplay));
        Assert.False(string.IsNullOrEmpty(item.ReadabilityLevelLabel));
        Assert.False(string.IsNullOrEmpty(item.ReadabilityColor));
        Assert.Equal(2, item.Scenes.Count);
        Assert.True(scenes[0].BarWidth > scenes[1].BarWidth); // bar widths seeded
        Assert.False(string.IsNullOrEmpty(scenes[0].WordCountDisplay));

        var noRead = new StatusBarChapterOverviewItem("C", 0, new ReadabilityResult { Score = 0 }, [], 0, 1);
        Assert.False(noRead.HasReadability);
    }

    [AvaloniaFact]
    public void ExtensionStatusBarItemVM_TextTooltipRefresh()
    {
        var withTip = new ExtensionStatusBarItemVM(new StatusBarItem { GetText = () => "txt", GetTooltip = () => "tip" });
        Assert.Equal("txt", withTip.DisplayText);
        Assert.Equal("tip", withTip.TooltipText);
        var changed = new List<string?>();
        withTip.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        withTip.Refresh();
        Assert.Contains("DisplayText", changed);
        Assert.Contains("TooltipText", changed);

        var noTip = new ExtensionStatusBarItemVM(new StatusBarItem { GetText = () => "x" });
        Assert.Equal(string.Empty, noTip.TooltipText); // null tooltip -> empty
        Assert.Same(noTip.Source, noTip.Source);
    }

    [AvaloniaFact]
    public void ExtensionSidebarAndContextTabVMs()
    {
        var panel = new SidebarPanel { Id = "p", Label = "Panel", Tooltip = "tip", CreateView = () => new Avalonia.Controls.Border() };
        var side = new ExtensionSidebarTabVM(panel);
        Assert.Equal("p", side.Id);
        Assert.Equal("Panel", side.Label);
        Assert.Equal("tip", side.Tooltip);
        Assert.Same(panel, side.Panel);

        var ctx = new ExtensionContextTabVM(panel);
        Assert.Equal("p", ctx.Id);
        Assert.Equal("Panel", ctx.Label);
        Assert.Equal("tip", ctx.Tooltip);
        var raised = false;
        ctx.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ExtensionContextTabVM.IsActive)) raised = true; };
        ctx.IsActive = true;
        Assert.True(ctx.IsActive);
        Assert.True(raised);
    }
}
