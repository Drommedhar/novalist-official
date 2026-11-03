using Novalist.Sdk.Hooks;

namespace Novalist.TestExtension;

internal sealed class GatedInlineAction(string gate) : IInlineActionContributor
{
    public IReadOnlyList<InlineActionDescriptor> GetInlineActions()
        => [new() { Id = "test.define", Label = "Define", Group = "Look up" }];

    public async Task<InlineActionResult> ExecuteAsync(string actionId, InlineActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            while (!File.Exists(gate))
                await Task.Delay(50, cancellationToken);
            return new InlineActionResult
            {
                Text = $"Definition of {request.SelectedText}",
                Disposition = InlineActionDisposition.ShowInformation,
                SourceLabel = "Wiktionary · CC BY-SA 4.0",
                SourceUrl = "https://en.wiktionary.org/wiki/happy"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new InlineActionResult { Error = "Cancelled." };
        }
    }
}
