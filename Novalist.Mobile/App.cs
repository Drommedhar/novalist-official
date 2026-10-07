using Novalist.Mobile.Pages;

namespace Novalist.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new RendererHostPage();
        var window = new Window(page) { Title = "Novalist" };
        window.Deactivated += (_, _) => (window.Page as RendererHostPage)?.RequestBackgroundSave();
        window.Stopped += (_, _) => (window.Page as RendererHostPage)?.RequestBackgroundSave();
        window.Destroying += (_, _) => (window.Page as RendererHostPage)?.Dispose();
        return window;
    }
}
