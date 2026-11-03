namespace Novalist.Sync.Client;

public sealed record HostedDeviceChallenge(Guid UserId, Guid DeviceId, Guid Nonce, string ServerUrl);
public sealed record HostedInvitationGrant(Guid ProjectId);
public sealed record HostedInvitationRequest(Guid InvitationId, DateTimeOffset ExpiresAt);
public sealed record HostedInvitationActivationRequest(Guid InvitationId, byte[] CertificateHash);
public sealed record HostedInvitationActivation(Guid ProjectId, Guid DeviceId, byte[] CertificateHash);
