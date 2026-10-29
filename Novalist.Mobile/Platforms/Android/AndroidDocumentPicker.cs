using Android.Content;
using Uri = Android.Net.Uri;

namespace Novalist.Mobile.Services;

internal static class AndroidDocumentPicker
{
    private const ActivityFlags Access = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
    private static TaskCompletionSource<string?>? _selection;

    public static Task<string?> PickAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var source = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _selection, source, null) != null)
            throw new InvalidOperationException("A folder picker is already open.");
        try
        {
            if (Platform.CurrentActivity is not MainActivity activity) throw new InvalidOperationException("No Android window is available.");
            activity.LaunchDocumentTreePicker();
        }
        catch
        {
            Interlocked.CompareExchange(ref _selection, null, source);
            throw;
        }
        return source.Task;
    });

    internal static Intent CreateIntent() => new Intent(Intent.ActionOpenDocumentTree)
        .AddFlags(Access | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

    internal static void CompleteSelection(Intent? result)
    {
        var source = Interlocked.Exchange(ref _selection, null);
        if (source == null) return;
        if (result?.Data is not Uri uri) { source.TrySetResult(null); return; }
        try
        {
            var access = result.Flags & Access;
            if (access != Access) throw new UnauthorizedAccessException("The selected folder must allow reading and writing.");
            var resolver = Platform.AppContext.ContentResolver ?? throw new InvalidOperationException("No document provider is available.");
            resolver.TakePersistableUriPermission(uri, access);
            source.TrySetResult(uri.ToString() ?? throw new IOException("The provider returned an invalid folder URI."));
        }
        catch (Exception error) { source.TrySetException(error); }
    }
}
