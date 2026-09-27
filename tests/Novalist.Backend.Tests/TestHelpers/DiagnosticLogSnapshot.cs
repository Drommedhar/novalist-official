namespace Novalist.Backend.Tests.TestHelpers;

internal static class DiagnosticLogSnapshot
{
    internal static string Read(string path)
    {
        // Backend work can keep logging after an RPC replies. File.ReadAllText
        // denies concurrent writes on Windows, even when the writer shares reads.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
