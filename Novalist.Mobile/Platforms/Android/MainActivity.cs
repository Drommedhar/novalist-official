using Android.App;
using Android.Content.PM;
#if DEBUG
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
#endif

namespace Novalist.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
#if DEBUG
    private ActivityResultLauncher? _storageProbePicker;

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _storageProbePicker = RegisterForActivityResult(
            new ActivityResultContracts.OpenDocumentTree(),
            new StorageProbeSelection());
    }

    internal void LaunchStorageProbePicker()
    {
        if (_storageProbePicker == null) throw new InvalidOperationException("The folder picker is not ready.");
        _storageProbePicker.Launch(null);
    }

    private sealed class StorageProbeSelection : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result)
            => Services.AndroidStorageProbe.CompleteSelection(result as Android.Net.Uri);
    }
#endif
}
