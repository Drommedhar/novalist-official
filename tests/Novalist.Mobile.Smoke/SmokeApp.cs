using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Novalist.Mobile.Pages;
using Novalist.Mobile.Services;
using WebKit;

namespace Novalist.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new RendererHostPage();
        var window = new Window(page) { Title = "Novalist collaboration test" };
        window.Deactivated += (_, _) => page.RequestBackgroundSave();
        window.Stopped += (_, _) => page.RequestBackgroundSave();
        window.Destroying += (_, _) => page.Dispose();
        window.Created += (_, _) => _ = ProbeAsync(page);
        return window;
    }

    private static async Task ProbeAsync(RendererHostPage page)
    {
        var output = Path.Combine(FileSystem.Current.AppDataDirectory, "collaboration-result.json");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var web = (HybridWebView)page.Content!;
            Task<string> Evaluate(string script) => MainThread.InvokeOnMainThreadAsync(async () =>
                (await ((WKWebView)web.Handler!.PlatformView!).EvaluateJavaScriptAsync(script))?.ToString() ?? "");
            while (web.Handler?.PlatformView is not WKWebView || await Evaluate("Boolean(window.novalistStores?.shell.getState().backendVersion)") != "1")
                await Task.Delay(250, deadline.Token);
            var script = await File.ReadAllTextAsync(Path.Combine(FileSystem.Current.AppDataDirectory, "collaboration-smoke.js"), deadline.Token);
            var credentialsPath = Path.Combine(FileSystem.Current.AppDataDirectory, "collaboration-credentials.json");
            if (File.Exists(credentialsPath))
            {
                using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(credentialsPath, deadline.Token));
                var host = new MobileCollaborationHost((_, _) => throw new NotSupportedException());
                await host.RequestAsync("collaboration/credentials", credentials.RootElement, deadline.Token);
            }
            await Evaluate(script);
            string result;
            var lastProgress = "";
            do
            {
                await Task.Delay(250, deadline.Token);
                result = await Evaluate("JSON.stringify(window.mobileSmokeResult ?? null)");
                var progress = await Evaluate("JSON.stringify({stage:window.mobileSmokeStage, busy:window.novalistStores.project.getState().workspaceBusy, tab:window.novalistStores.shell.getState().mobileTab})");
                if (progress != lastProgress)
                {
                    await File.WriteAllTextAsync(Path.Combine(FileSystem.Current.AppDataDirectory, "collaboration-progress.json"), progress, deadline.Token);
                    lastProgress = progress;
                }
            } while (result == "null");
            await File.WriteAllTextAsync(output, result, deadline.Token);
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { ok = false, error = error.ToString() }));
        }
    }
}
