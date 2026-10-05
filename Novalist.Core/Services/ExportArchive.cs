using System.IO.Compression;
using System.Text;

namespace Novalist.Core.Services;

/// <summary>Writes text and binary entries shared by EPUB and DOCX packages.</summary>
internal static class ExportArchive
{
    internal static async Task WriteEntryAsync(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    internal static async Task WriteBinaryEntryAsync(ZipArchive zip, string path, string sourceFile)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var source = File.OpenRead(sourceFile);
        await source.CopyToAsync(target);
    }
}
