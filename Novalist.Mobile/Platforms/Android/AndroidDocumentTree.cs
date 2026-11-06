using Android.Content;
using Android.Database;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace Novalist.Mobile.Services;

internal sealed class AndroidDocumentTree : IDocumentTree
{
    private readonly string treeUri;
    public AndroidDocumentTree(string treeUri) => this.treeUri = treeUri;
    private static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified
    ];
    private static ContentResolver Resolver => Platform.AppContext.ContentResolver
        ?? throw new InvalidOperationException("No document provider is available.");
    private Uri Tree => Parse(treeUri);
    public string RootId => Serialize(DocumentsContract.BuildDocumentUriUsingTree(Tree, DocumentsContract.GetTreeDocumentId(Tree)));

    public void EnsureAccess()
    {
        if (!Resolver.PersistedUriPermissions.Any(permission => permission.Uri?.ToString() == treeUri
                && permission.IsReadPermission && permission.IsWritePermission))
            throw new UnauthorizedAccessException("Access to this folder expired. Choose it again using Open folder.");
    }

    public IReadOnlyList<DocumentTreeEntry> GetChildren(string parentId) => Invoke<IReadOnlyList<DocumentTreeEntry>>(() =>
    {
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(Tree, DocumentsContract.GetDocumentId(Parse(parentId)))
            ?? throw new IOException("The provider did not return a folder URI.");
        using var cursor = Query(uri);
        var result = new List<DocumentTreeEntry>();
        while (cursor.MoveToNext())
        {
            DocumentTreeReadCancellation.Current.ThrowIfCancellationRequested();
            result.Add(Entry(cursor));
        }
        DocumentTreeReadCancellation.Current.ThrowIfCancellationRequested();
        return result;
    });

    public DocumentTreeEntry Create(string parentId, string name, bool directory) => Invoke(() =>
    {
        var mime = directory ? DocumentsContract.Document.MimeTypeDir
            : Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(Path.GetExtension(name).TrimStart('.').ToLowerInvariant())
                ?? "application/octet-stream";
        var uri = DocumentsContract.CreateDocument(Resolver, Parse(parentId), mime, name)
            ?? throw new IOException("The provider could not create the document.");
        return ReadEntry(uri).RequireName(name);
    });

    public byte[] Read(string id) => Invoke(() => ReadWithCancellation(cancellationToken =>
    {
        using var signal = new Android.OS.CancellationSignal();
        using var cancelOpen = cancellationToken.Register(() => CancelRead(signal.Cancel));
        using var descriptor = Resolver.OpenAssetFileDescriptor(Parse(id), "r", signal)
            ?? throw new IOException("The provider did not open the document.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = descriptor.CreateInputStream()
                ?? throw new IOException("The provider did not open the document stream.");
            using var cancelRead = cancellationToken.Register(() => CancelRead(descriptor.Close));
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = input.Read(buffer, 0, buffer.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0) break;
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        finally { descriptor.Close(); }
    }));

    public void Write(string id, byte[] bytes) => Invoke(() =>
    {
        using var output = Resolver.OpenOutputStream(Parse(id), "wt") ?? throw new IOException("The provider did not open the document for writing.");
        output.Write(bytes);
        output.Flush();
        return true;
    });

    public DocumentTreeEntry Rename(string id, string name) => Invoke(() =>
    {
        var uri = DocumentsContract.RenameDocument(Resolver, Parse(id), name)
            ?? throw new IOException("This provider could not rename the document safely.");
        return ReadEntry(uri).RequireName(name);
    });

    public void Delete(string id) => Invoke(() =>
    {
        if (!DocumentsContract.DeleteDocument(Resolver, Parse(id))) throw new IOException("The provider could not delete the document.");
        return true;
    });

    private DocumentTreeEntry ReadEntry(Uri uri)
    {
        using var cursor = Query(uri);
        if (!cursor.MoveToFirst()) throw new FileNotFoundException("The document is no longer available.");
        return Entry(cursor);
    }

    private static ICursor Query(Uri uri) => ReadWithCancellation(token => Query(uri, token));

    private static ICursor Query(Uri uri, CancellationToken cancellationToken)
    {
        using var signal = new Android.OS.CancellationSignal();
        using var cancelQuery = cancellationToken.Register(() => CancelRead(signal.Cancel));
        var cursor = Resolver.Query(uri, Columns, null, null, null, signal) ?? throw new IOException("The provider did not return document information.");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var changes = new FolderChanges();
        ICursor? loading = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var error = cursor.Extras?.GetString(DocumentsContract.ExtraError);
                if (!string.IsNullOrEmpty(error)) throw new IOException($"The document provider could not load this folder: {error}");
                if (cursor.Extras?.GetBoolean(DocumentsContract.ExtraLoading) != true) return cursor;
                var remaining = TimeSpan.FromSeconds(30) - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new IOException("The document provider timed out loading this folder. Check your connection and try again.");
                if (loading == null)
                {
                    loading = cursor;
                    loading.RegisterContentObserver(changes);
                }
                else
                {
                    cursor.Dispose();
                    changes.Wait(remaining, cancellationToken);
                }
                // Requery immediately after subscribing to cover completion before observer registration.
                // Keep the first cursor alive: closing it can cancel the provider's download.
                changes.Reset();
                cancellationToken.ThrowIfCancellationRequested();
                cursor = Resolver.Query(uri, Columns, null, null, null, signal)
                    ?? throw new IOException("The provider did not return document information.");
            }
        }
        catch { if (!ReferenceEquals(cursor, loading)) cursor.Dispose(); throw; }
        finally
        {
            if (loading != null) { loading.UnregisterContentObserver(changes); loading.Dispose(); }
        }
    }

    private sealed class FolderChanges : ContentObserver
    {
        private readonly object _gate = new();
        private bool _changed;
        public FolderChanges() : base(null) { }
        public override void OnChange(bool selfChange)
        {
            lock (_gate) { _changed = true; Monitor.PulseAll(_gate); }
        }
        public void Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var cancellation = cancellationToken.Register(() =>
            {
                lock (_gate) Monitor.PulseAll(_gate);
            });
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_changed) Monitor.Wait(_gate, timeout);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        public void Reset()
        {
            lock (_gate) _changed = false;
        }
    }

    private DocumentTreeEntry Entry(ICursor cursor)
    {
        var id = cursor.GetString(0) ?? throw new IOException("The provider returned a document without an identifier.");
        var name = cursor.GetString(1) ?? throw new IOException("The provider returned a document without a name.");
        var directory = cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir;
        var size = cursor.IsNull(3) ? 0 : cursor.GetLong(3);
        var modified = cursor.IsNull(4) || cursor.GetLong(4) <= 0
            ? DateTime.UnixEpoch : DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(4)).UtcDateTime;
        return new DocumentTreeEntry(Serialize(DocumentsContract.BuildDocumentUriUsingTree(Tree, id)), name, directory, size, modified);
    }

    private static Uri Parse(string value) => Uri.Parse(value) ?? throw new IOException("The saved document URI is invalid.");
    private static string Serialize(Uri? uri) => uri?.ToString() ?? throw new IOException("The provider returned an invalid document URI.");
    private static void CancelRead(Action cancel)
    {
        try { cancel(); }
        catch (Exception error)
        {
            // Cancellation callbacks must not escape into the RPC notification handler.
            Android.Util.Log.Warn("NovalistFolderCancel", $"Document read cancellation cleanup failed ({error.GetType().Name}).");
        }
    }

    private static T ReadWithCancellation<T>(Func<CancellationToken, T> action)
    {
        var cancellationToken = DocumentTreeReadCancellation.Current;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action(cancellationToken);
        }
        catch (Exception error) when (cancellationToken.IsCancellationRequested && error is not OperationCanceledException)
        {
            throw new OperationCanceledException("The document read was canceled.", error, cancellationToken);
        }
    }

    private static T Invoke<T>(Func<T> action)
    {
        try { return action(); }
        catch (Java.Lang.SecurityException error) { throw new UnauthorizedAccessException("Access to this folder was revoked. Choose it again using Open folder.", error); }
        catch (Java.IO.IOException error) { throw new IOException("The document provider could not complete the operation.", error); }
    }
}
