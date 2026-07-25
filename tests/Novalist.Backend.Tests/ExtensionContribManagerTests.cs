using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Backend.Extensions;
using Novalist.Sdk.Hooks;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers the contribution-surfacing + execution helpers added to
/// <see cref="ExtensionManager"/> for the Electron host, exercised against the
/// real sample extension (Novalist.Sdk.Example).
/// </summary>
[Collection("BackendStatics")]
public class ExtensionContribManagerTests
{
    private const string SampleId = "com.novalist.writingtoolkit";

    private static void DeploySample(string extRoot)
    {
        var folder = Path.Combine(extRoot, "Sample");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(folder, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(folder, "extension.json"),
            $$"""{ "id": "{{SampleId}}", "name": "Sample", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
    }

    private static async Task<(ExtensionManager Mgr, HostServices Host)> LoadSampleAsync(string extRoot)
    {
        DeploySample(extRoot);
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns(Task.CompletedTask);
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var mgr = new ExtensionManager(settings, host, new ExtensionLoader(extRoot));
        host.ExtensionManager = mgr;
        await mgr.LoadAllAsync();
        return (mgr, host);
    }

    [Fact]
    public async Task InlineActions_AreSurfacedInPriorityOrder_AndExecuteById()
    {
        using var ext = new TempDir();
        var (mgr, _) = await LoadSampleAsync(ext.Path);

        var descriptors = mgr.GetInlineActionDescriptors();
        Assert.Equal(2, descriptors.Count);
        Assert.Equal("ext.writingtoolkit.uppercase", descriptors[0].Id);
        Assert.Equal("ext.writingtoolkit.wordcount", descriptors[1].Id);

        var upper = await mgr.ExecuteInlineActionAsync(
            "ext.writingtoolkit.uppercase",
            new InlineActionRequest { SelectedText = "hello" },
            CancellationToken.None);
        Assert.NotNull(upper);
        Assert.Equal("HELLO", upper!.Text);
        Assert.Equal(InlineActionDisposition.ReplaceSelection, upper.Disposition);

        var count = await mgr.ExecuteInlineActionAsync(
            "ext.writingtoolkit.wordcount",
            new InlineActionRequest { SelectedText = "a b c" },
            CancellationToken.None);
        Assert.NotNull(count);
        Assert.Equal(InlineActionDisposition.InsertAfterSelection, count!.Disposition);

        var unknown = await mgr.ExecuteInlineActionAsync(
            "does.not.exist", new InlineActionRequest(), CancellationToken.None);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task ContextMenuItems_EnumerateAndExecute_RespectingVisibility()
    {
        using var ext = new TempDir();
        var (mgr, host) = await LoadSampleAsync(ext.Path);

        var items = mgr.EnumerateContextMenuItemsWithIds().ToList();
        Assert.Equal(2, items.Count);
        var chapterId = items.First(i => i.Item.Context == "Chapter").Id;
        var sceneId = items.First(i => i.Item.Context == "Scene").Id;

        var activations = 0;
        host.ContentViewActivated += (_, _) => activations++;

        // Chapter item (no visibility guard) runs with any context.
        mgr.ExecuteContextMenuItem(chapterId, context: null);
        Assert.Equal(1, activations);

        // Scene item is hidden without a scene context (IsVisible returns false).
        mgr.ExecuteContextMenuItem(sceneId, context: null);
        Assert.Equal(1, activations);

        // With a concrete context it runs.
        mgr.ExecuteContextMenuItem(sceneId, context: new object());
        Assert.Equal(2, activations);

        // Unknown id is a no-op.
        mgr.ExecuteContextMenuItem("bogus#ctx#9", context: new object());
        Assert.Equal(2, activations);
    }

    [Fact]
    public async Task StatusBarItems_EnumerateAndExecute()
    {
        using var ext = new TempDir();
        var (mgr, _) = await LoadSampleAsync(ext.Path);

        var items = mgr.EnumerateStatusBarItemsWithIds().ToList();
        var item = Assert.Single(items);
        Assert.Equal($"{SampleId}#sb#writingToolkit.pomodoro", item.Id);
        var idleText = item.Item.GetText();

        mgr.ExecuteStatusBarItem(item.Id); // starts the timer
        var runningText = mgr.EnumerateStatusBarItemsWithIds().Single().Item.GetText();
        Assert.NotEqual(idleText, runningText);

        mgr.ExecuteStatusBarItem(item.Id); // stops it again (cleanup)
        mgr.ExecuteStatusBarItem("bogus#sb#x"); // no-op
    }

    [Fact]
    public async Task Themes_AreEnumerated()
    {
        using var ext = new TempDir();
        var (mgr, _) = await LoadSampleAsync(ext.Path);

        var themes = mgr.EnumerateThemes().ToList();
        Assert.Equal(2, themes.Count);
        Assert.Contains(themes, t => t.Theme.Name == "Sepia");
        Assert.All(themes, t => Assert.Equal(SampleId, t.ExtensionId));
    }

    [Fact]
    public async Task SettingsSchema_EnumerateApplyAndUnknownNoOp()
    {
        using var ext = new TempDir();
        var (mgr, _) = await LoadSampleAsync(ext.Path);

        var schemas = mgr.EnumerateSettingsSchemas().ToList();
        var schema = Assert.Single(schemas);
        Assert.Equal(SampleId, schema.ExtensionId);
        var fields = schema.Contributor.GetSettingsSchema().Fields;
        Assert.Contains(fields, f => f.Key == "duration");

        await mgr.ApplySettingsSchemaAsync(SampleId, new Dictionary<string, string> { ["duration"] = "40" });
        var updated = mgr.EnumerateSettingsSchemas().Single().Contributor.GetSettingsSchema();
        Assert.Equal("40", updated.Fields.First(f => f.Key == "duration").Value);

        // Unknown extension id: no-op (no throw).
        await mgr.ApplySettingsSchemaAsync("not.an.extension", new Dictionary<string, string>());
    }
}
