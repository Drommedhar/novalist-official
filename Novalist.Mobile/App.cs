using Novalist.Mobile.Pages;

namespace Novalist.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new RendererHostPage()) { Title = "Novalist" };
}
