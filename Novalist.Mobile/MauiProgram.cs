namespace Novalist.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if ANDROID
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<Pages.AndroidNavigationScrollView, Pages.AndroidNavigationScrollHandler>());
#endif
        builder.UseMauiApp<App>();
        return builder.Build();
    }
}
