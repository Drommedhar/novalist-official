using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Core.Services;
using Novalist.Mobile.Services;
#if IOS
using CoreGraphics;
using UIKit;
using UniformTypeIdentifiers;
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private Task SetNativeColorsAsync(JsonElement args)
    {
#if ANDROID
        return MainThread.InvokeOnMainThreadAsync(() => SetAndroidColors(ArgString(args, 0), ArgString(args, 1)));
#else
        return Task.CompletedTask;
#endif
    }

    private async Task<object?> SetNavVisibleAsync(JsonElement args)
    {
        // Show the native Liquid Glass navigation only inside a project
        // (hidden on the welcome/start screen). Whichever chrome the
        // current size class uses - bottom tab bar or leading sidebar -
        // follows this flag, and the web's inset follows with it.
        var visible = args.ValueKind == JsonValueKind.Array
            && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.True;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
        _navVisible = visible;
        var regular = _isRegularWidth == true;
        if (_tabBar != null) _tabBar.Hidden = regular || !visible;
        if (_sidebar != null) _sidebar.Hidden = !regular || !visible;
        if (!visible) HidePlanMenu();
        UpdateSidebarScrim();
        PushChromeMetrics();
                });
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => { _androidNavVisible = visible; PushAndroidLayout(); });
#endif
        return null;
    }

    private async Task<object?> SetSidebarTitlesAsync(JsonElement args)
    {
        // args[0] = localized titles in SidebarItems order. Same contract as
        // setTabTitles, for the iPad sidebar.
        var titles = new List<string>();
        if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var el in args[0].EnumerateArray())
                titles.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
        }
#if IOS
                _sidebarTitles = titles.ToArray();
                await MainThread.InvokeOnMainThreadAsync(ApplySidebarTitles);
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => SetAndroidTitles(_androidDestinations, titles));
#endif
        return null;
    }

    private async Task<object?> SetSidebarCollapsedAsync(JsonElement args)
    {
        // Collapse the iPad sidebar to an icon-only rail (or expand it).
        // Driven from the web so the toggle lives with the other pane
        // controls in the tablet top bar.
        var collapsed = args.ValueKind == JsonValueKind.Array
            && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.True;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
        if (_sidebarCollapsed == collapsed) return;
        _sidebarCollapsed = collapsed;
        ApplySidebarCollapsed();
                });
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => { _androidSidebarCollapsed = collapsed; PushAndroidLayout(); });
#endif
        return null;
    }

    private async Task<object?> SetSidebarSelectionAsync(JsonElement args)
    {
        // Keep the sidebar highlight on the destination the web actually
        // shows (e.g. opening a scene from the binder switches to Write).
        var key = ArgString(args, 0);
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() => SelectSidebarKey(key));
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => SelectAndroidButton(_androidDestinations, Array.IndexOf(AndroidDestinations, key)));
#endif
        return null;
    }

    private async Task<object?> RequestLayoutAsync()
    {
        // The web asks which layout it is in on mount. The size-class pass
        // may have run before the bundle finished loading, so re-push it
        // unconditionally rather than only on change.
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
        _isRegularWidth = null;      // force ApplySizeClass to re-push
        ApplySizeClass();
                });
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(PushAndroidLayout);
#endif
        return null;
    }

    private async Task<object?> SetPlanningMenuOpenAsync(JsonElement args)
    {
        // args[0]=open (bool), args[1]=localized labels (in Plan-menu order).
        // Rendered natively so it uses the same Liquid Glass as the tab bar
        // and can anchor to the Plan tab item. Selection/dismissal come back
        // via window.__novalistPlanSelect / __novalistPlanDismiss.
        var open = args.ValueKind == JsonValueKind.Array
            && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.True;
        var labels = new List<string>();
        if (open && args.GetArrayLength() > 1 && args[1].ValueKind == JsonValueKind.Array)
            foreach (var el in args[1].EnumerateArray())
                labels.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
        if (open) ShowPlanMenu(labels.ToArray());
        else HidePlanMenu();
                });
#elif ANDROID
        if (open) await MainThread.InvokeOnMainThreadAsync(() => ShowAndroidPlanMenuAsync(labels, ArgString(args, 2)));
#endif
        return null;
    }

    private async Task<object?> SetSelectedTabAsync(JsonElement args)
    {
        // The bar highlights whatever was tapped. A tab the web switched
        // to on its own (the first-run tour walks them) never was, so it
        // has to be told, or the highlight names one tab while the screen
        // shows another.
        var index = args.ValueKind == JsonValueKind.Array
            && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.Number
            ? args[0].GetInt32()
            : -1;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
        if (_tabBar?.Items is not { } items) return;
        if (index < 0 || index >= items.Length) return;
        _tabBar.SelectedItem = items[index];
        _committedItem = items[index];
                });
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => SelectAndroidButton(_androidTabs, index));
#endif
        return null;
    }

    private async Task<object?> SetTabTitlesAsync(JsonElement args)
    {
        // args[0] = localized titles in tab order (dashboard, manuscript,
        // codex, search, more). Pushed by the web on mount + language change.
        var titles = new List<string>();
        if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
            && args[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var el in args[0].EnumerateArray())
                titles.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
        }
#if IOS
                _tabTitles = titles.ToArray();
                await MainThread.InvokeOnMainThreadAsync(ApplyTabTitles);
#elif ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => SetAndroidTitles(_androidTabs, titles));
#endif
        return null;
    }
}
