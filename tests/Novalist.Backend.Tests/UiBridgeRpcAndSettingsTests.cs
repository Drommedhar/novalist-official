using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Sdk.Models.Wizards;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Extension settings/wizard RPC surface and the renderer→host UI bridge callbacks.</summary>
public sealed class UiBridgeRpcAndSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public UiBridgeRpcAndSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-uibrpc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Settings.LoadAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<ExtensionsRpc> LoadSampleAsync()
    {
        var dir = Path.Combine(_root, "exts", "Sample");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(dir, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(dir, "extension.json"),
            """{"id":"com.novalist.sample","name":"Sample","version":"1.0.0","entryAssembly":"Novalist.Sdk.Example.dll"}""");
        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(Path.Combine(_root, "exts"));
        var rpc = new ExtensionsRpc(_workspace);
        await rpc.LoadAsync();
        return rpc;
    }

    [Fact]
    public async Task SettingsPages_And_Wizards_AreExposed()
    {
        var rpc = await LoadSampleAsync();

        var pages = rpc.SettingsPages();
        Assert.Contains(pages, p => p.ExtensionId == "com.novalist.sample" && !string.IsNullOrEmpty(p.Category));

        var wizards = rpc.Wizards();
        Assert.Contains(wizards, w => w.WizardId == "com.novalist.writingtoolkit.pomodoro");
    }

    [Fact]
    public async Task RunWizard_UnknownWizard_ReturnsNull()
    {
        var rpc = await LoadSampleAsync();
        Assert.Null(await rpc.RunWizardAsync("com.novalist.sample", "does.not.exist"));
        Assert.Null(await rpc.RunWizardAsync("com.missing", "x"));
    }

    [Fact]
    public async Task RunWizard_CompletesViaBridge()
    {
        var rpc = await LoadSampleAsync();

        string? token = null;
        _workspace.UiBridge.Notifier = (method, payload) =>
        {
            if (method == "ui/wizard/open" && payload is WizardOpenDto dto)
                token = dto.Token;
            return Task.CompletedTask;
        };

        var task = rpc.RunWizardAsync("com.novalist.sample", "com.novalist.writingtoolkit.pomodoro");
        Assert.NotNull(token);
        _workspace.UiBridge.CompleteWizard(token!, new WizardResult { DefinitionId = "x", Completed = true });

        var result = await task;
        Assert.True(result!.Completed);
    }

    [Fact]
    public void UiBridgeRpc_RoutesToBridge()
    {
        var bridgeRpc = new UiBridgeRpc(_workspace);

        // Progress cancel routes through.
        var handle = _workspace.UiBridge.CreateProgress(new BusyProgressOptions { AllowCancel = true });
        var cancelled = false;
        handle.Cancelled += () => cancelled = true;
        // We need the token; capture it from a fresh progress with a notifier.
        string? token = null;
        _workspace.UiBridge.Notifier = (method, payload) =>
        {
            if (method == "ui/progress/open" && payload is ProgressOpenDto dto) token = dto.Token;
            return Task.CompletedTask;
        };
        var handle2 = _workspace.UiBridge.CreateProgress(new BusyProgressOptions { AllowCancel = true });
        var cancelled2 = false;
        handle2.Cancelled += () => cancelled2 = true;
        bridgeRpc.ProgressCancel(token!);
        Assert.True(cancelled2);
        Assert.False(cancelled);
        handle.Dispose();
        handle2.Dispose();
    }

    [Fact]
    public async Task UiBridgeRpc_WizardCallbacks_RouteToBridge()
    {
        var bridgeRpc = new UiBridgeRpc(_workspace);
        string? token = null;
        _workspace.UiBridge.Notifier = (method, payload) =>
        {
            if (method == "ui/wizard/open" && payload is WizardOpenDto dto) token = dto.Token;
            return Task.CompletedTask;
        };

        var def = new WizardDefinition
        {
            Id = "w",
            Steps =
            {
                new ChoiceStep
                {
                    Id = "c",
                    DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>(
                        new[] { new WizardChoice { Value = "v", Label = "L" } })
                },
                new TextStep { Id = "t", Validator = _ => Task.FromResult<string?>("nope") },
            }
        };
        var task = _workspace.UiBridge.RunWizardAsync(def, null);

        var choices = await bridgeRpc.WizardChoices(token!, "c", new WizardResult());
        Assert.Single(choices);
        Assert.Equal("nope", await bridgeRpc.WizardValidate(token!, "t", new WizardResult()));

        bridgeRpc.WizardComplete(token!, null);
        Assert.Null(await task);
    }

    [Fact]
    public void PickComplete_HandsThePathToTheBridge()
    {
        // The renderer's answer has to reach the task the extension is awaiting;
        // without this route a picker would open and never resolve.
        var task = _workspace.UiBridge.PickAsync("folder", "Anywhere", false);
        new Novalist.Backend.Rpc.UiBridgeRpc(_workspace).PickComplete(
            TokenOfLastPick(), "picked");

        Assert.Equal("picked", task.Result);
    }

    /// <summary>
    /// The token of the pick just opened. Read back off the bridge rather than
    /// guessed, because it is a fresh guid each time.
    /// </summary>
    private string TokenOfLastPick()
    {
        var field = typeof(Novalist.Backend.Extensions.UiBridge).GetField(
            "_pickers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var pickers = (System.Collections.IEnumerable)field.GetValue(_workspace.UiBridge)!;
        foreach (var entry in pickers)
            return (string)entry.GetType().GetProperty("Key")!.GetValue(entry)!;
        throw new InvalidOperationException("No pick was open.");
    }
}
