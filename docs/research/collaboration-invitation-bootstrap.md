# Hosted invitation bootstrap — implementation design

Status: client-library bootstrap and certificate verification are implemented;
desktop joining, membership rotation and recovery remain unfinished. This extends the community
server's planned device verification to support the accepted requirement that
someone can join for the first time while the owner is offline. It is not an
complete implemented security guarantee.

## Constraints

- An invitation admits one signed-in account. The same account can retry.
- The server receives neither project keys nor invitation private keys.
- An invited writer can edit but cannot issue owner authorizations.
- Devices sign updates separately. A shared project encryption key alone must
  not authorize impersonating another member's device.
- Closing a local session does not delete the hosted project or other members'
  access. Reconnecting the same session retains pending edits; creating another
  session does not replay the old session's queue.

## Proposed identities and authorization

Each installation/account has an Ed25519 signing key, an X25519 receiving key,
and a key-bound device UUID. Private keys live in the OS-encrypted credential store,
outside project folders. The local server extension now binds immutable public
keys to the authenticated account and requires fresh connection proofs before
project operations. Registration, linking and connection proofs bind the chosen
canonical server URL to prevent relaying them to another server. Additional
devices require an active device's linking signature. These checks establish
device/account possession; clients still verify project membership separately
against owner-authorized keys.

The first owner device anchors a project identity. Clients pin that public key;
an invitation carries it through the channel the owner uses to share the link.
Changing this anchor requires an explicit recovery/verification workflow.

An owner creates a random invitation ID, a separate public member ID and an
Ed25519 delegation key. The owner signs an authorization binding the project,
member ID, delegation public key, contributor role, epoch and validity interval.
The invitation ID remains a secret server-access capability: it must never be
published as a member identity. Display names and the server's internal account
identifiers are not cryptographic identities.

The private invitation fragment contains the owner anchor, signed authorization,
delegation private key, project epoch keys, and the checkpoint reference/hash.
It is never sent in an HTTP URL. Redeeming sends the invitation ID in the existing
authenticated request body. The recipient persists this bootstrap before
claiming it, so a lost response does not strand a consumed invitation.

Before claiming, the recipient signs a device certificate using the
delegation key. It binds the project, member ID, device UUID, signing public key
and receiving public key. Claim and activation must be atomic and bind the
initial certificate fingerprint to the claiming account. A lost response can be
retried by that account and certificate after the invitation expires, provided
membership and the device remain active; retry must not create membership again.
Peers verify the owner authorization and delegated certificate against the
pinned anchor before trusting any update signature.
The owner device uses a certificate signed directly by the pinned owner key.

No chain may delegate an owner role or authorize another invitation. An invited
member's certificate cannot claim a different member ID. Extra devices need
explicit linking or recovery; they must not be accepted merely because they
present the same OIDC account. Certificate storage is public metadata; it may
not contain project titles, content, or private keys.

Signatures cover fixed, length-checked binary encodings with domain strings for
each record, UUIDs in the existing protocol's big-endian layout, and explicit
version/role fields. The JSON transport representation is never signed directly.
Use NSec's existing Ed25519/X25519 implementations, not custom curve arithmetic.
[NSec Key API](https://nsec.rocks/docs/api/nsec.cryptography.key),
[Ed25519](https://www.rfc-editor.org/rfc/rfc8032),
[X25519](https://www.rfc-editor.org/rfc/rfc7748).

## Lifecycle requirements before enabling sharing

- Invitation expiry/revocation controls server access. Possession of an old
  bootstrap already reveals its historical keys; expiry cannot erase copied
  data. Future access revocation requires a new epoch, excluding removed members
  and revoked invitation capabilities from key distribution.
- Every epoch transition retires outstanding invitations atomically. Their
  private bootstrap cannot supply the new epoch key; the owner can issue a new
  link. The server must retain the referenced checkpoint and dependencies while
  a link remains usable, or serve an authenticated replacement.
- Member removal needs an owner-signed membership/epoch transition, atomic
  server authorization changes, fresh checkpoints, and key wrapping for the
  remaining verified receiving keys. Clients reject unsigned replacements.
- Certificates remain available to verify retained history. Revoked devices
  cannot sign new-epoch edits; clients distinguish history from live authority.
  A transition pins the exact retained device keys. An old delegation key cannot
  authorize an extra device in the new epoch. Additional devices require linking
  by a currently authorized device or independent recovery credentials.
- A valid but unknown device triggers certificate retrieval and validation,
  without advancing past an unvalidated update in the durable catch-up cursor.
- Device secrets, delegated member secrets, epoch keys, checkpoint references and
  scene identities must survive restart. Recovery must restore the same trust
  anchors, not silently create a different identity for an existing project.
- A claimed invitation's authorization remains a member credential; its expiry
  limits first claim, not the member's existing sessions. Whether any retained
  invitation secret can link additional devices is subject to the explicit
  linking/recovery rules, not an unrestricted registration endpoint.

## Required review questions

1. Can a current member or server substitute another member's device, role or
   receiving key without the client detecting it?
2. Are invitation claim, device proof and certificate registration bound tightly
   enough to prevent cross-account claims, retries with changed keys and revoked
   device re-registration?
3. Can an expired or revoked invitation obtain future keys? How are outstanding
   invitations handled during rotation while the owner is offline?
4. Do checkpoint publication and onboarding remain recoverable after each crash
   boundary, without reusing a nonce/sequence or dropping a consumed capability?

These questions must be resolved in the implementation tests and review before
the sharing controls are enabled. Authentication and editor tests alone do not
establish a complete invitation or membership protocol.

## Implemented certificate and bootstrap slice

Device IDs use the first 16 bytes of SHA-256 over ASCII
`novalist/v1/device-id`, the 32-byte Ed25519 public key, then the 32-byte X25519
public key. Set the UUID version nibble to 8 and variant bits to `10`, and read
the UUID in network byte order. This uses the custom layout permitted by
[RFC 9562 §5.8](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.8).
Certificate verification checks this binding before accepting a previously
unknown device. Device credential storage is version 2; earlier unreleased
random-UUID credentials are rejected, never silently reassigned.

The owner grant body is 86 bytes: version 1, role 2 (contributor), project UUID,
member UUID, uint32 epoch, int64 Unix start time, int64 Unix expiry, and 32-byte
delegation public key. Integers and UUIDs are big-endian. Its Ed25519 signature
covers ASCII `novalist/v1/project-grant` followed by that body. The signed grant
is 150 bytes. Certificate bodies begin with version, role (1 owner or 2
contributor), project UUID, member UUID, epoch, device UUID, 32-byte signing key,
and 32-byte receiving key: 118 bytes. Contributors append the signed grant.
The final 64-byte signature covers ASCII `novalist/v1/project-device` followed
by the whole body. Owners sign directly; contributors use the delegation key.
Verification rejects future-dated grants. Expiry prevents a new local claim;
existing certificates remain valid for that epoch after invitation expiry.

The encrypted frame contains version 1, a uint16 certificate length, the
certificate, then a fragmentation packet. Fragmentation v2 adds an explicit
uint32 chunk capacity to the previous 25-byte packet header, so certificate
overhead stays inside the server's payload limit. The client still reads v1.
Session journals pin the owner/epoch and exact device certificates atomically
with accepted data; changing those pins or downgrading to a legacy session
fails. The current implementation supports one epoch per session.

`novalist://collaboration/join#...` carries bounded base64url JSON, including the
private bootstrap. It is parsed locally and never fetched as an HTTP URL. The
joining device persists its certificate, epoch key, checkpoint and invitation
identity through the OS-encrypted credential interface before activation.
Pending invitations have separate records, with a pinned owner/epoch, so a
revoked or expired first attempt can be replaced without discarding the
credentials needed to retry an ambiguous successful claim. Active credentials
are saved only after activation, and another invitation cannot overwrite them.
Activation checks that the transport authenticates the prepared device before
sending the claim.
Activation sends only the invitation ID and SHA-256 certificate fingerprint;
the verified server connection binds it to one account and device. Retries
reuse the persisted certificate. The API-level local server test covers owner
disconnect, joining credentials restored from storage, checkpoint download,
and new writer edits accepted when the owner reconnects. Desktop URI dispatch
and create/join controls are not implemented yet.

## Certificates inside encrypted updates

To minimize server changes, a bounded device certificate accompanies every
encrypted client payload. The server would continue to store opaque v1 envelopes.
For an unknown sender, the client authenticates/decrypts the envelope with
the project key into a private buffer, validate only the certificate against its
pinned owner/epoch authorization, and then verifies the outer Ed25519 signature.
No document bytes, cursor movement or journal advancement may be delivered until
all checks pass. The certificate's project, device UUID, epoch and role must match
the authenticated header and pinned authorization; conflicting keys for an
existing UUID are rejected. Possession of the project key alone would not
authorize a writer. This deliberately changes the server design's §8.3 receive
order only for bounded certificate verification; application content remains
inaccessible until signature and authorization checks succeed.

This avoids requiring a new server key directory merely to validate a sender.
It does not remove the need for atomic invitation activation, revocation/epoch
APIs, client-pinned transitions, historical keys or authenticated key wrapping.
Transitions and key wraps must authenticate through the previous epoch's pinned
owner/device keys, never through a certificate that requires the new key to read.
The accepted predecessor and epoch are persisted to reject rollback.
Single-use claims are enforced by the authenticated server; a malicious server
colluding with someone who already holds the private invitation cannot prove to
other clients which account claimed first. The cryptographic guarantee is that
the server alone cannot fabricate an owner grant or an authorized device.
