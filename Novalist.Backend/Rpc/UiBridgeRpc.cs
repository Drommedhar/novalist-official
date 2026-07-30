using Novalist.Backend.Extensions;
using Novalist.Sdk.Models.Wizards;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Renderer → host callbacks for the interactive UI bridge: progress-dialog
/// cancellation and the wizard round-trip (dynamic choices, step validation,
/// completion). Host → renderer traffic goes out as <c>ui/*</c> notifications
/// (see <see cref="UiBridge"/>).
/// </summary>
public sealed class UiBridgeRpc
{
    private readonly Workspace _workspace;

    public UiBridgeRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private UiBridge Bridge => _workspace.UiBridge;

    [JsonRpcMethod("ui/progress/cancel")]
    public void ProgressCancel(string token) => Bridge.CancelProgress(token);

    [JsonRpcMethod("ui/wizard/choices")]
    public Task<IReadOnlyList<WizardChoiceDto>> WizardChoices(string token, string stepId, WizardResult partial)
        => Bridge.WizardChoicesAsync(token, stepId, partial);

    [JsonRpcMethod("ui/wizard/validate")]
    public Task<string?> WizardValidate(string token, string stepId, WizardResult partial)
        => Bridge.WizardValidateAsync(token, stepId, partial);

    [JsonRpcMethod("ui/wizard/complete")]
    public void WizardComplete(string token, WizardResult? result) => Bridge.CompleteWizard(token, result);

    /// <summary>The renderer reporting what a file or folder dialog returned.</summary>
    [JsonRpcMethod("ui/pick/complete")]
    public void PickComplete(string token, string? path) => Bridge.CompletePick(token, path);
}
