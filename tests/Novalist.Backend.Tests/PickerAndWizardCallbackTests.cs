using System.Collections.Concurrent;
using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The folder and file pickers, and the wizard completion callback.
///
/// Both exist because of the same gap: an extension could ask the writer a
/// question and had no way to act on the answer. The wizard collected answers
/// and handed them to whoever started the run - which for a wizard reached from
/// the command palette is not the extension - and the only way to ask for a path
/// was a text box the writer typed into by hand.
/// </summary>
public class PickerAndWizardCallbackTests
{
    private static (UiBridge Bridge, ConcurrentQueue<(string Method, object? Payload)> Sent) Bridge()
    {
        var sent = new ConcurrentQueue<(string, object?)>();
        var bridge = new UiBridge
        {
            Notifier = (method, payload) => { sent.Enqueue((method, payload)); return Task.CompletedTask; }
        };
        return (bridge, sent);
    }

    private static HostServices Host()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        return new HostServices(
            Substitute.For<IFileService>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(),
            settings);
    }

    // ── Pickers over the bridge ──

    [Fact]
    public async Task AFolderRequestReachesTheRendererAndComesBackWithAPath()
    {
        var (bridge, sent) = Bridge();

        var task = bridge.PickAsync("folder", "Where to publish", false);

        Assert.True(sent.TryDequeue(out var message));
        Assert.Equal("ui/pick/open", message.Method);
        var payload = Assert.IsType<PickOpenDto>(message.Payload);
        Assert.Equal("folder", payload.Kind);
        Assert.Equal("Where to publish", payload.Title);
        Assert.False(payload.Images);

        bridge.CompletePick(payload.Token, @"C:\sites\salt-road");

        Assert.Equal(@"C:\sites\salt-road", await task);
    }

    [Fact]
    public async Task AFileRequestCarriesWhetherOnlyImagesAreWanted()
    {
        var (bridge, sent) = Bridge();

        var task = bridge.PickAsync("file", "Pick a cover", true);

        sent.TryDequeue(out var message);
        var payload = Assert.IsType<PickOpenDto>(message.Payload);
        Assert.Equal("file", payload.Kind);
        Assert.True(payload.Images);

        bridge.CompletePick(payload.Token, "cover.png");
        Assert.Equal("cover.png", await task);
    }

    [Fact]
    public async Task CancellingTheDialogAnswersWithNothingRatherThanWaitingForEver()
    {
        var (bridge, sent) = Bridge();
        var task = bridge.PickAsync("folder", "Anywhere", false);
        sent.TryDequeue(out var message);
        var token = ((PickOpenDto)message.Payload!).Token;

        bridge.CompletePick(token, null);

        Assert.Null(await task);
    }

    [Fact]
    public async Task AnEmptyPathIsTheSameAsCancelling()
    {
        // The renderer reports an empty string in some cancel paths, and an
        // extension treating "" as a folder would write into its own directory.
        var (bridge, sent) = Bridge();
        var task = bridge.PickAsync("folder", "Anywhere", false);
        sent.TryDequeue(out var message);

        bridge.CompletePick(((PickOpenDto)message.Payload!).Token, "   ");

        Assert.Null(await task);
    }

    [Fact]
    public void AnswerForATokenNobodyIsWaitingOnIsIgnored()
    {
        var (bridge, _) = Bridge();

        // A second answer for a dialog already resolved, or a stale one after a
        // reload. Neither should throw.
        bridge.CompletePick("never-asked", "somewhere");
    }

    [Fact]
    public async Task TwoDialogsAtOnceDoNotCrossTheirAnswers()
    {
        var (bridge, sent) = Bridge();

        var first = bridge.PickAsync("folder", "First", false);
        var second = bridge.PickAsync("file", "Second", false);

        sent.TryDequeue(out var one);
        sent.TryDequeue(out var two);
        var firstToken = ((PickOpenDto)one.Payload!).Token;
        var secondToken = ((PickOpenDto)two.Payload!).Token;
        Assert.NotEqual(firstToken, secondToken);

        bridge.CompletePick(secondToken, "second.txt");
        bridge.CompletePick(firstToken, "first-folder");

        Assert.Equal("first-folder", await first);
        Assert.Equal("second.txt", await second);
    }

    // ── Pickers on the host facade ──

    [Fact]
    public async Task WithNoPickerWiredTheAnswerIsNothingRatherThanAHang()
    {
        // Which is what a headless run looks like: no renderer, so no dialog.
        using var host = Host();

        Assert.Null(await host.PickFolderAsync("Anywhere"));
        Assert.Null(await host.PickFileAsync("Anything"));
    }

    [Fact]
    public async Task TheHostPassesTheKindTitleAndImageFlagThrough()
    {
        using var host = Host();
        var asked = new List<(string Kind, string Title, bool Images)>();
        host.Picker = (kind, title, images) =>
        {
            asked.Add((kind, title, images));
            return Task.FromResult<string?>("answer");
        };

        Assert.Equal("answer", await host.PickFolderAsync("A folder"));
        Assert.Equal("answer", await host.PickFileAsync("An image", images: true));

        Assert.Equal(("folder", "A folder", false), asked[0]);
        Assert.Equal(("file", "An image", true), asked[1]);
    }

    [Fact]
    public async Task ANullTitleIsPassedAsEmptyRatherThanNull()
    {
        using var host = Host();
        string? seen = null;
        host.Picker = (_, title, _) => { seen = title; return Task.FromResult<string?>(null); };

        await host.PickFolderAsync(null!);

        Assert.Equal(string.Empty, seen);
    }

    // ── The wizard completion callback ──

    [Fact]
    public async Task FinishingAWizardTellsTheExtensionThatDefinedIt()
    {
        // Without this a contributed wizard is a form that goes nowhere.
        using var host = Host();
        WizardResult? told = null;
        var definition = new WizardDefinition
        {
            Id = "w1",
            OnCompleted = result => { told = result; return Task.CompletedTask; }
        };
        host.WizardLauncher = (_, _) => Task.FromResult<WizardResult?>(
            new WizardResult { DefinitionId = "w1", Completed = true });

        await host.RunWizardAsync(definition);

        Assert.NotNull(told);
        Assert.Equal("w1", told!.DefinitionId);
    }

    [Fact]
    public async Task CancellingAWizardTellsNobody()
    {
        using var host = Host();
        var told = false;
        var definition = new WizardDefinition
        {
            Id = "w1",
            OnCompleted = _ => { told = true; return Task.CompletedTask; }
        };

        // Cancelled outright.
        host.WizardLauncher = (_, _) => Task.FromResult<WizardResult?>(null);
        await host.RunWizardAsync(definition);
        Assert.False(told);

        // Or abandoned part way, which is not the same as finishing.
        host.WizardLauncher = (_, _) => Task.FromResult<WizardResult?>(
            new WizardResult { DefinitionId = "w1", Completed = false });
        await host.RunWizardAsync(definition);
        Assert.False(told);
    }

    [Fact]
    public async Task AWizardWithNoCallbackStillReturnsItsAnswers()
    {
        using var host = Host();
        host.WizardLauncher = (_, _) => Task.FromResult<WizardResult?>(
            new WizardResult { DefinitionId = "w1", Completed = true });

        var result = await host.RunWizardAsync(new WizardDefinition { Id = "w1" });

        Assert.True(result!.Completed);
    }

    [Fact]
    public async Task AnExtensionThatThrowsWhileActingOnItsWizardDoesNotTakeTheCallerDown()
    {
        using var host = Host();
        var definition = new WizardDefinition
        {
            Id = "w1",
            OnCompleted = _ => throw new InvalidOperationException("boom")
        };
        host.WizardLauncher = (_, _) => Task.FromResult<WizardResult?>(
            new WizardResult { DefinitionId = "w1", Completed = true });

        // The answers still come back: the writer filled the wizard in, and the
        // extension's own bug is not their problem.
        var result = await host.RunWizardAsync(definition);

        Assert.True(result!.Completed);
    }
}
