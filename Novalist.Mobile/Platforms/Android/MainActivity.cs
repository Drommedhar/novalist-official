using Android.App;
using Android.Content.PM;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;

namespace Novalist.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    private ActivityResultLauncher? _documentTreePicker;
#if DEBUG
    private ActivityResultLauncher? _storageProbePicker;
#endif

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _documentTreePicker = RegisterForActivityResult(
            new ActivityResultContracts.StartActivityForResult(), new DocumentTreeSelection());
#if DEBUG
        _storageProbePicker = RegisterForActivityResult(
            new ActivityResultContracts.OpenDocumentTree(),
            new StorageProbeSelection());
#endif
    }

    internal void LaunchDocumentTreePicker()
    {
        if (_documentTreePicker == null) throw new InvalidOperationException("The folder picker is not ready.");
        _documentTreePicker.Launch(Services.AndroidDocumentPicker.CreateIntent());
    }

    private sealed class DocumentTreeSelection : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result)
            => Services.AndroidDocumentPicker.CompleteSelection(result is ActivityResult { ResultCode: (int)Result.Ok } selection ? selection.Data : null);
    }

#if DEBUG
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
