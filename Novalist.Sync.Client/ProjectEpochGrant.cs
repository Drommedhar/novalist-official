using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed record EpochGrantBody(int Version, Guid ProjectId, uint PreviousEpoch, uint Epoch, long FirstServerSequence,
    Guid GrantorDeviceId, Guid RecipientDeviceId, byte[] Certificate, CheckpointReference Checkpoint, WrappedEpochKey Key);
internal sealed record SignedEpochGrant(EpochGrantBody Body, byte[] Signature);

internal static class ProjectEpochGrant
{
    internal static byte[] Create(HostedProjectAccess previous, HostedProjectAccess next, DeviceIdentity owner,
        AuthorizedProjectDevice recipient, byte[] certificate, CheckpointReference checkpoint)
    {
        var wrapped = EpochKeyWrapping.Wrap(new(next.ProjectId, next.Epoch, recipient.DeviceId, owner.Id),
            next.State.EpochKey, recipient.ReceivingKey, owner.SigningKey);
        var body = new EpochGrantBody(1, next.ProjectId, previous.Epoch, next.Epoch, next.FirstServerSequence,
            owner.Id, recipient.DeviceId, certificate, checkpoint, wrapped);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedEpochGrant(body, SignatureAlgorithm.Ed25519.Sign(owner.SigningKey, Encode(body))));
    }

    internal static HostedProjectAccess Open(HostedProjectAccess previous, DeviceIdentity device, StoredHostedEpochGrant stored)
    {
        if (stored.Payload.Length > 8192) throw new CryptographicException("Invalid epoch grant size.");
        var grant = JsonSerializer.Deserialize<SignedEpochGrant>(stored.Payload) ?? throw new CryptographicException("Missing epoch grant.");
        var body = grant.Body;
        var owner = PublicKey.Import(SignatureAlgorithm.Ed25519, previous.State.OwnerKey, KeyBlobFormat.RawPublicKey);
        if (body is null || body.Version != 1 || body.ProjectId != previous.ProjectId || body.RecipientDeviceId != device.Id ||
            body.Epoch <= previous.Epoch || body.PreviousEpoch >= body.Epoch || body.Epoch != stored.Epoch ||
            body.GrantorDeviceId != stored.GrantorDeviceId || body.FirstServerSequence != stored.FirstServerSeq ||
            body.FirstServerSequence <= previous.FirstServerSequence || grant.Signature is not { Length: 64 } ||
            !SignatureAlgorithm.Ed25519.Verify(owner, Encode(body), grant.Signature))
            throw new CryptographicException("The project key transition is not authorized by its pinned owner.");
        var key = EpochKeyWrapping.Unwrap(new(body.ProjectId, body.Epoch, device.Id, body.GrantorDeviceId), body.Key, device.ReceivingKey, owner);
        try
        {
            return new(previous.State with { Epoch = body.Epoch, EpochKey = key, Certificate = body.Certificate,
                Checkpoint = body.Checkpoint, FirstServerSequence = body.FirstServerSequence, InvitationId = null, InvitationHash = null }, device);
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    private static byte[] Encode(EpochGrantBody body) => [.. "novalist/v1/epoch-transition"u8, .. JsonSerializer.SerializeToUtf8Bytes(body)];
}
