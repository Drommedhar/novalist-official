namespace Novalist.Sync.Client;

public sealed partial class TcpPeerTransport
{
    public long BootstrapSequence { get; private set; }

    internal async Task<PeerBootstrapDescription> BeginBootstrapAsync(CancellationToken ct)
    {
        var response = await RequestAsync(new(NextId(), "bootstrap-begin", Access.ProjectId), ct, TimeSpan.FromMinutes(10));
        var description = response.Bootstrap ?? throw new InvalidDataException("Missing project comparison details.");
        if (description.ManifestLength is <= 0 or > 8 * 1024 * 1024 || description.Sequence < 0 ||
            description.ManifestSha256 is not { Length: 64 } || !description.ManifestSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid project comparison details.");
        BootstrapSequence = description.Sequence;
        return description;
    }

    internal async Task<byte[]> ReadBootstrapAsync(string entry, long offset, CancellationToken ct)
    {
        var response = await RequestAsync(new(NextId(), "bootstrap-read", Access.ProjectId, BootstrapEntry: entry, Offset: offset), ct);
        return response.Data ?? throw new InvalidDataException("Missing project content.");
    }
}
