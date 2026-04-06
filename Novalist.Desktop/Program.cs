using Avalonia;
using Avalonia.WebView.Desktop;
using System;
using System.IO;

namespace Novalist.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Novalist");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            File.WriteAllText(logPath, $"{DateTime.UtcNow:O}\n{ex}");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseDesktopWebView()
            .WithDeveloperTools(options =>
            {
//#if DEBUG
                //options.ConnectOnStartup = true;
//#endif
            });
}
