using System.Collections.Concurrent;
using Novalist.Backend.Extensions;
using Novalist.Sdk.Models.Wizards;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public class UiBridgeTests
{
    private static (UiBridge Bridge, ConcurrentQueue<(string Method, object? Payload)> Sent) Build()
    {
        var sent = new ConcurrentQueue<(string, object?)>();
        var bridge = new UiBridge
        {
            Notifier = (method, payload) => { sent.Enqueue((method, payload)); return Task.CompletedTask; }
        };
        return (bridge, sent);
    }

    [Fact]
    public void ShowNotification_SendsMessage()
    {
        var (bridge, sent) = Build();
        bridge.ShowNotification("hello");
        Assert.True(sent.TryDequeue(out var msg));
        Assert.Equal("ui/showNotification", msg.Method);
        Assert.Equal("hello", msg.Payload);
    }

    [Fact]
    public void ChangeSignals_ReachTheInterfaceUnderTheNamesItListensFor()
    {
        // The renderer subscribes to these two strings by name. A mismatch
        // fails nothing: the interface just never reloads, and an extension's
        // write looks like it did not happen.
        var (bridge, sent) = Build();

        bridge.EntitiesChanged();
        Assert.True(sent.TryDequeue(out var entities));
        Assert.Equal("entities/changed", entities.Method);

        bridge.ProjectStructureChanged();
        Assert.True(sent.TryDequeue(out var structure));
        Assert.Equal("project/structureChanged", structure.Method);
    }

    [Fact]
    public void CreateProgress_OpensDialog_AndDrivesUpdates()
    {
        var (bridge, sent) = Build();
        var handle = bridge.CreateProgress(new BusyProgressOptions
        {
            Title = "T", InitialStatus = "S", IsIndeterminate = false,
            ShowProgressBar = true, AllowCancel = true, CancelLabel = "Stop", IsModal = true
        });

        Assert.True(sent.TryDequeue(out var open));
        Assert.Equal("ui/progress/open", open.Method);
        var dto = Assert.IsType<ProgressOpenDto>(open.Payload);
        Assert.Equal("T", dto.Title);
        Assert.True(dto.AllowCancel);
        Assert.False(handle.IsClosed);

        handle.SetStatus("working");
        handle.SetProgress(2.0);   // clamps to 1
        handle.SetTitle("T2");
        handle.SetIndeterminate(true);
        handle.SetDetails(new[] { "a", "b" });
        handle.SetDetails(null);   // -> empty list

        var updates = DrainUpdates(sent, dto.Token);
        Assert.Equal("status", updates[0].Field);
        Assert.Equal(1.0, Assert.IsType<double>(updates[1].Value));
        Assert.Equal("title", updates[2].Field);
        Assert.Equal("indeterminate", updates[3].Field);
        Assert.Equal("details", updates[4].Field);

        handle.Dispose();
        Assert.True(handle.IsClosed);
        Assert.Contains(Snapshot(sent), s => s.Method == "ui/progress/close");

        // After close, updates are suppressed.
        sent.Clear();
        handle.SetStatus("late");
        Assert.Empty(Snapshot(sent));
        handle.Dispose(); // idempotent
    }

    [Fact]
    public void CancelProgress_FiresCancelled_AndToken()
    {
        var (bridge, sent) = Build();
        var handle = bridge.CreateProgress(new BusyProgressOptions { AllowCancel = true });
        var token = ((ProgressOpenDto)Snapshot(sent)[0].Payload!).Token;

        var cancelled = false;
        handle.Cancelled += () => cancelled = true;
        Assert.False(handle.CancellationToken.IsCancellationRequested);

        bridge.CancelProgress(token);
        Assert.True(cancelled);
        Assert.True(handle.CancellationToken.IsCancellationRequested);

        bridge.CancelProgress("unknown-token"); // no-op, no throw

        // Cancel after dispose is a no-op.
        handle.Dispose();
        bridge.CancelProgress(token); // handle already removed -> no throw
    }

    [Fact]
    public async Task RunWizard_OpensAndCompletes()
    {
        var (bridge, sent) = Build();
        var def = new WizardDefinition { Id = "w1", Steps = { new TextStep { Id = "a" } } };
        var task = bridge.RunWizardAsync(def, seed: null);

        Assert.True(sent.TryDequeue(out var open));
        Assert.Equal("ui/wizard/open", open.Method);
        var payload = Assert.IsType<WizardOpenDto>(open.Payload);
        Assert.Equal("w1", payload.Definition.Id);
        Assert.False(task.IsCompleted);

        bridge.CompleteWizard(payload.Token, new WizardResult { DefinitionId = "w1", Completed = true });
        var result = await task;
        Assert.True(result!.Completed);

        bridge.CompleteWizard(payload.Token, null); // already removed -> no-op
    }

    [Fact]
    public async Task RunWizard_CancelledReturnsNull()
    {
        var (bridge, sent) = Build();
        var task = bridge.RunWizardAsync(new WizardDefinition { Id = "w" }, null);
        var token = ((WizardOpenDto)Snapshot(sent)[0].Payload!).Token;
        bridge.CompleteWizard(token, null);
        Assert.Null(await task);
    }

    [Fact]
    public async Task WizardChoices_InvokesDynamicProvider()
    {
        var (bridge, sent) = Build();
        var step = new ChoiceStep
        {
            Id = "model",
            DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>(
                new[] { new WizardChoice { Value = "m1", Label = "Model 1" } })
        };
        var task = bridge.RunWizardAsync(new WizardDefinition { Id = "w", Steps = { step } }, null);
        var token = ((WizardOpenDto)Snapshot(sent)[0].Payload!).Token;

        var choices = await bridge.WizardChoicesAsync(token, "model", new WizardResult());
        Assert.Equal("m1", Assert.Single(choices).Value);

        // Unknown token / non-choice / no-provider all return empty.
        Assert.Empty(await bridge.WizardChoicesAsync("nope", "model", new WizardResult()));
        Assert.Empty(await bridge.WizardChoicesAsync(token, "missing", new WizardResult()));

        bridge.CompleteWizard(token, null);
        await task;
    }

    [Fact]
    public async Task WizardChoices_ChoiceStepWithoutProvider_Empty()
    {
        var (bridge, sent) = Build();
        var task = bridge.RunWizardAsync(
            new WizardDefinition { Id = "w", Steps = { new ChoiceStep { Id = "c" } } }, null);
        var token = ((WizardOpenDto)Snapshot(sent)[0].Payload!).Token;
        Assert.Empty(await bridge.WizardChoicesAsync(token, "c", new WizardResult()));
        bridge.CompleteWizard(token, null);
        await task;
    }

    [Fact]
    public async Task WizardValidate_InvokesValidator()
    {
        var (bridge, sent) = Build();
        var step = new TextStep
        {
            Id = "url",
            Validator = r => Task.FromResult<string?>(r.GetText("url") == "ok" ? null : "bad url")
        };
        var task = bridge.RunWizardAsync(new WizardDefinition { Id = "w", Steps = { step } }, null);
        var token = ((WizardOpenDto)Snapshot(sent)[0].Payload!).Token;

        var bad = new WizardResult();
        bad.Answers["url"] = new WizardAnswer { Text = "x" };
        Assert.Equal("bad url", await bridge.WizardValidateAsync(token, "url", bad));

        var good = new WizardResult();
        good.Answers["url"] = new WizardAnswer { Text = "ok" };
        Assert.Null(await bridge.WizardValidateAsync(token, "url", good));

        // Unknown token / step without validator -> null.
        Assert.Null(await bridge.WizardValidateAsync("nope", "url", good));

        bridge.CompleteWizard(token, null);
        await task;
    }

    [Fact]
    public async Task WizardValidate_StepWithoutValidator_Null()
    {
        var (bridge, sent) = Build();
        var task = bridge.RunWizardAsync(
            new WizardDefinition { Id = "w", Steps = { new TextStep { Id = "t" } } }, null);
        var token = ((WizardOpenDto)Snapshot(sent)[0].Payload!).Token;
        Assert.Null(await bridge.WizardValidateAsync(token, "t", new WizardResult()));
        bridge.CompleteWizard(token, null);
        await task;
    }

    [Fact]
    public void DefaultNotifier_IsNoop()
    {
        var bridge = new UiBridge();
        bridge.ShowNotification("x"); // no notifier set -> no throw
    }

    private static List<(string Method, object? Payload)> Snapshot(
        ConcurrentQueue<(string Method, object? Payload)> sent) => sent.ToList();

    private static List<ProgressUpdateDto> DrainUpdates(
        ConcurrentQueue<(string Method, object? Payload)> sent, string token) =>
        sent.Where(s => s.Method == "ui/progress/update")
            .Select(s => (ProgressUpdateDto)s.Payload!)
            .Where(u => u.Token == token)
            .ToList();
}
