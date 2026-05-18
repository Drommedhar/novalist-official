using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Novalist.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Avalonia.Controls.WebView on Linux ships only a GtkX11 adapter, so
        // GTK must initialise against X11 (XWayland on Wayland sessions).
        // Without this, the WebView throws "Unable to initialize GTK" on every
        // Wayland-default distro. Avalonia's own backend ignores GDK_BACKEND.
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");

            // WebKitGTK 2.42+ defaults to a DMA-BUF compositor that needs a
            // working GBM device. Under XWayland (and in many VMs/containers)
            // GBM buffer allocation fails ("Failed to create GBM buffer …
            // Invalid argument") and the WebView renders as a blank surface
            // with no error. Force the software fallback renderer.
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        }

        if (Environment.GetEnvironmentVariable("NOVALIST_VERBOSE") == "1")
        {
            Console.Error.WriteLine($"[env] GDK_BACKEND={Environment.GetEnvironmentVariable("GDK_BACKEND")}");
            Console.Error.WriteLine($"[env] WEBKIT_DISABLE_DMABUF_RENDERER={Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER")}");
            Console.Error.WriteLine($"[env] WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS={Environment.GetEnvironmentVariable("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS")}");
            Console.Error.WriteLine($"[env] APPIMAGE={Environment.GetEnvironmentVariable("APPIMAGE")}");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    internal static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Novalist");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            File.AppendAllText(logPath, $"{DateTime.UtcNow:O} [{source}]\n{ex}\n\n");
        }
        catch { /* best effort */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .WithDeveloperTools(options =>
            {
//#if DEBUG
                //options.ConnectOnStartup = true;
//#endif
            });
}
