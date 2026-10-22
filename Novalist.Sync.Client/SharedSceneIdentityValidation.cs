using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Sync.Client;

internal static partial class SharedSceneIdentityValidation
{
    internal static async Task ValidateAsync(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var prefix = new char[4096];
        var count = await reader.ReadBlockAsync(prefix.AsMemory(), ct);
        var content = new string(prefix, 0, count);
        if (!content.StartsWith("<!--nv ", StringComparison.Ordinal)) return;
        var marker = Header().Match(content);
        if (!marker.Success) throw new InvalidDataException("Unsupported shared scene identity.");
        var id = marker.Groups[1].Value;
        SharedProjectPath.Validate(id);
        if (id.Contains('/')) throw new InvalidDataException("A scene identity cannot contain a storage path.");
    }

    [GeneratedRegex(@"^<!--nv v=1 id=(\S+) -->")]
    private static partial Regex Header();
}
