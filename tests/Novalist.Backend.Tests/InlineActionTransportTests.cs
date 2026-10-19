using System.Text.Json;
using Nerdbank.Streams;
using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class InlineActionTransportTests : IDisposable
{
    private readonly TempDir _settings = new();
    private readonly BackendHost _host;
    private readonly JsonRpc _client;
    private readonly TaskCompletionSource<ProgressOpenDto> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public InlineActionTransportTests()
    {
        _host = new BackendHost(_settings.Path);
        _host.Workspace.ExtensionsLoaderOverride = new ExtensionLoader(_settings.Combine("Extensions"));
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        _host.Attach(serverStream, serverStream);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        _client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter));
        _client.AddLocalRpcMethod("ui/progress/open", new Action<ProgressOpenDto>(value => _opened.TrySetResult(value)));
        _client.AddLocalRpcMethod("ui/progress/close", new Action<string>(value => _closed.TrySetResult(value)));
        _client.StartListening();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
        _settings.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PositionalEditorArgumentsReachTheContributor(bool includeContext)
    {
        var contributor = Substitute.For<IInlineActionContributor>();
        contributor.GetInlineActions().Returns([new InlineActionDescriptor { Id = "lookup" }]);
        contributor.ExecuteAsync("lookup", Arg.Any<InlineActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InlineActionResult
            {
                Text = "A definition.",
                Disposition = InlineActionDisposition.ShowInformation,
                Alternatives = ["An alternative."],
                AsSuggestion = true,
                SourceLabel = "Wiktionary · CC BY-SA 4.0",
                SourceUrl = "https://de.wiktionary.org/wiki/Prolog"
            });
        await _host.Workspace.ExtensionsHost.LoadAllAsync();
        _host.Workspace.HostServices!.RegisterInlineActionContributor(contributor);
        object[] arguments = includeContext
            ? ["lookup", "word", "chapter", "scene", "Earlier prose.", "A direction."]
            : ["lookup", "word", "chapter", "scene"];

        var result = await _client.InvokeAsync<InlineActionResultDto>("extensions/inlineAction/execute", arguments);

        Assert.Equal("A definition.", result.Text);
        Assert.Equal("information", result.Disposition);
        Assert.Equal(["An alternative."], result.Alternatives);
        Assert.True(result.AsSuggestion);
        Assert.Equal("Wiktionary · CC BY-SA 4.0", result.SourceLabel);
        Assert.Equal("https://de.wiktionary.org/wiki/Prolog", result.SourceUrl);
        await contributor.Received(1).ExecuteAsync("lookup", Arg.Is<InlineActionRequest>(request =>
            request.SelectedText == "word" && request.ChapterGuid == "chapter" && request.SceneId == "scene"
            && request.PrecedingText == (includeContext ? "Earlier prose." : "")
            && request.Directive == (includeContext ? "A direction." : "")),
            Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("empty")]
    [InlineData("error")]
    [InlineData("cancel")]
    public async Task ProgressSpansTheRequestAndCanCancelIt(string outcome)
    {
        var completion = new TaskCompletionSource<InlineActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contributor = Substitute.For<IInlineActionContributor>();
        contributor.GetInlineActions().Returns([new InlineActionDescriptor { Id = "lookup", Label = "Define" }]);
        contributor.ExecuteAsync("lookup", Arg.Any<InlineActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                try { return await completion.Task.WaitAsync(call.Arg<CancellationToken>()); }
                catch (OperationCanceledException) { return new InlineActionResult { Error = "Cancelled" }; }
            });
        await _host.Workspace.ExtensionsHost.LoadAllAsync();
        _host.Workspace.HostServices!.RegisterInlineActionContributor(contributor);

        var pending = _client.InvokeAsync<InlineActionResultDto>(
            "extensions/inlineAction/execute", "lookup", "word", "chapter", "scene", "", "");
        var progress = await _opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Define", progress.Title);
        Assert.True(progress.IsIndeterminate);
        Assert.True(progress.ShowProgressBar);
        Assert.True(progress.AllowCancel);
        Assert.False(pending.IsCompleted);

        if (outcome == "cancel")
            await _client.NotifyAsync("ui/progress/cancel", progress.Token);
        else if (outcome == "error")
            completion.SetException(new InvalidOperationException("Lookup failed"));
        else if (outcome == "empty")
            completion.SetResult(null!);
        else
            completion.SetResult(new InlineActionResult { Text = "A definition." });

        if (outcome == "error")
            await Assert.ThrowsAsync<RemoteInvocationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            if (outcome == "empty") Assert.Null(result);
            else if (outcome == "success") Assert.Equal("A definition.", result.Text);
            else Assert.Equal("Cancelled", result.Error);
        }
        Assert.Equal(progress.Token, await _closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CancellingTheRpcCancelsTheContributor()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contributor = Substitute.For<IInlineActionContributor>();
        contributor.GetInlineActions().Returns([new InlineActionDescriptor { Id = "lookup" }]);
        contributor.ExecuteAsync("lookup", Arg.Any<InlineActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new InlineActionResult();
            });
        await _host.Workspace.ExtensionsHost.LoadAllAsync();
        _host.Workspace.HostServices!.RegisterInlineActionContributor(contributor);
        using var cancellation = new CancellationTokenSource();
        var pending = _client.InvokeWithCancellationAsync<InlineActionResultDto>(
            "extensions/inlineAction/execute", ["lookup", "word", "chapter", "scene", "", ""], cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
