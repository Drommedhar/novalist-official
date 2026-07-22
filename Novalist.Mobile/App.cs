using Novalist.Mobile.Pages;

namespace Novalist.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        // Phase 1: the real renderer over the in-process backend bridge.
        // (SeamPage / EditorSpikePage remain in the project as Phase 0 probes.)
        new(new RendererHostPage()) { Title = "Novalist" };
}
