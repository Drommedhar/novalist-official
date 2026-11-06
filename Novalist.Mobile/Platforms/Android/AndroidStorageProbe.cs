#if DEBUG
using Android.Content;
using Android.Provider;
using System.Text;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace Novalist.Mobile.Services;

internal static class AndroidStorageProbe
{
    private const string StateKey = "android-storage-probe";
    private const string Contents = "Android folder round-trip: Grüße, 中文.";
    private const ActivityFlags Access = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
    private static TaskCompletionSource<Uri?>? _selection;
    private static ContentResolver Resolver => Platform.AppContext.ContentResolver ?? throw new InvalidOperationException("No document provider resolver.");

    internal static void CompleteSelection(Uri? uri) => Interlocked.Exchange(ref _selection, null)?.TrySetResult(uri);

    internal static async Task<object> RunAsync(string phase)
    {
        if (phase == "start")
        {
            var selected = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_selection != null) throw new InvalidOperationException("A probe picker is already open.");
                var source = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _selection = source;
                try
                {
                    if (Platform.CurrentActivity is not MainActivity activity) throw new InvalidOperationException("No Android activity.");
                    activity.LaunchStorageProbePicker();
                }
                catch { _selection = null; throw; }
                return source.Task;
            });
            if (selected == null) return new { cancelled = true };
            return await Task.Run(() => Start(selected));
        }
        var state = JsonSerializer.Deserialize<ProbeState>(Preferences.Default.Get(StateKey, ""))
            ?? throw new InvalidOperationException("Start a storage probe first.");
        return await Task.Run(() => phase switch
        {
            "resume" => Resume(state),
            "revoked" => CheckRevocation(state),
            _ => throw new ArgumentException("Unknown storage probe phase.")
        });
    }

    private static object Start(Uri tree)
    {
        if (Preferences.Default.ContainsKey(StateKey)) throw new InvalidOperationException("Finish the existing storage probe first.");
        Resolver.TakePersistableUriPermission(tree, Access);
        var root = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))
            ?? throw new IOException("No tree document URI.");
        var folder = Create(root, DocumentsContract.Document.MimeTypeDir, "Novalist-probe-" + Guid.NewGuid().ToString("N"));
        var nested = Create(folder, DocumentsContract.Document.MimeTypeDir, "Grüße");
        var file = Create(nested, "text/plain", "draft.txt");
        Write(file, "initial");
        Write(file, Contents);
        if (Read(file) != Contents) throw new IOException("The provider did not round-trip an updated document.");
        var renamed = DocumentsContract.RenameDocument(Resolver, file, "重命名.txt") ?? throw new IOException("Rename failed.");
        var moved = DocumentsContract.MoveDocument(Resolver, renamed, nested, folder) ?? throw new IOException("Move failed.");
        if (Read(moved) != Contents) throw new IOException("The moved document changed.");
        var children = Children(tree, folder);
        if (children != 2) throw new IOException("The provider returned an unexpected child listing.");
        Preferences.Default.Set(StateKey, JsonSerializer.Serialize(new ProbeState(Serialize(tree), Serialize(folder), Serialize(moved))));
        return new { phase = "restart", updated = true, renamed = true, moved = true, children };
    }

    private static object Resume(ProbeState state)
    {
        var file = Parse(state.File);
        if (Read(file) != Contents) throw new IOException("The persisted folder grant did not survive restart.");
        if (!DocumentsContract.DeleteDocument(Resolver, Parse(state.Folder))) throw new IOException("Probe cleanup failed.");
        Resolver.ReleasePersistableUriPermission(Parse(state.Tree), Access);
        return new { phase = "restartForRevocation", persisted = true, deleted = true };
    }

    private static object CheckRevocation(ProbeState state)
    {
        var tree = Parse(state.Tree);
        var root = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))
            ?? throw new IOException("No tree document URI.");
        try { Children(tree, root); }
        catch (Java.Lang.SecurityException)
        {
            Preferences.Default.Remove(StateKey);
            return new { phase = "complete", accessRevoked = true };
        }
        throw new InvalidOperationException("Folder access remains active; restart the app before checking revocation.");
    }

    private static int Children(Uri tree, Uri parent)
    {
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetDocumentId(parent))
            ?? throw new IOException("No child URI.");
        using var cursor = Resolver.Query(uri, [DocumentsContract.Document.ColumnDocumentId], null, null, null)
            ?? throw new IOException("The provider did not return a child listing.");
        return cursor.Count;
    }

    private static Uri Create(Uri parent, string mime, string name)
        => DocumentsContract.CreateDocument(Resolver, parent, mime, name) ?? throw new IOException("Document creation failed.");
    private static Uri Parse(string value) => Uri.Parse(value) ?? throw new IOException("Invalid stored probe URI.");
    private static string Serialize(Uri uri) => uri.ToString() ?? throw new IOException("The provider returned an invalid URI.");
    private static string Read(Uri uri)
    {
        using var stream = Resolver.OpenInputStream(uri) ?? throw new IOException("No document input stream.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
    private static void Write(Uri uri, string value)
    {
        using var stream = Resolver.OpenOutputStream(uri, "wt") ?? throw new IOException("No document output stream.");
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(value);
    }
    private sealed record ProbeState(string Tree, string Folder, string File);
}
#endif
