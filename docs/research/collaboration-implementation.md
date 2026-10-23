# Collaboration implementation record

Work is on `feature/realtime-collaboration`, based on main commit
`7bdd1cd7d4425a08287a2d0ae513e807b8b6cdfc`. The accepted scope is recorded in
[the task graph](../specs/realtime-collaboration.md) and ADR 0007, which supersedes
the earlier server-storage requirements. This record describes implementation progress; it is not a release
announcement.

## Relay correction, 2026-10-23

Follow-up host progress correction: the local checkpoint's processed byte count
was displayed as transferred data, and its completion was displayed as session
success while startup was still pending. The renderer now distinguishes local
preparation, pending startup and successful sharing. A real Share-button test
reproduced the incorrect labels; the corrected UI regression and an injected
late-startup failure test pass. The user also reported a later error whose text
was no longer available. That startup failure remains unidentified; the populated
project succeeds through the Share button using isolated copies. Local evidence
is retained in `app/out/host-progress-*.log`, `host-startup-green.log` and
`host-corpus-*.log`. No backend behavior changed in this correction.

The desktop now connects through the live packet relay in the local server
checkout at `2ad5a9f`. Its default mode registers no database and exposes no
project-storage endpoints. The host must remain online. Clients compare relative
paths and SHA-256 hashes, reuse matching local files, and request missing or
changed files in chunks. Local differences are retained in **Recovered edits**.
The desktop shows scanning, comparison and transfer progress, file/byte/reuse
counts, and cancellation, including while the workspace is busy. See
[ADR 0007](../adr/0007-relay-only-collaboration.md).

Tasks C9–C11 are complete for the local testing handoff. Final implementation
checkpoint: `3f76a493`.

The real existing-copy desktop test passes with a matching 2 MB asset, a changed
file, a missing asset larger than 1 MB, retained Git metadata, recovery copies and
host-offline rejection. An additional acceptance test used isolated copies of
the populated local project: all 117 active scenes matched byte-for-byte, 239 of
431 files were reused, and 1,088,090 bytes crossed the connection, including
collaboration state. Both sessions ended successfully and the original source
files were unchanged. The complete check took 1.4 minutes.

Ordinary read requests no longer rescan the project. Writes, synchronization
barriers, capture and session end retain validation and recovery for outside
edits. Explicit readmission preserves the device certificate trusted by existing
peers; removed devices still need a fresh valid invitation. Snapshot capture
wakes catch-up after success or failure so a joining writer cannot strand edits
received during capture.

| Validation | Result |
| --- | --- |
| Collaboration client | 169 tests pass. |
| Real relay integration | Seven transport tests pass against the local relay. |
| Desktop transport/document host | 142 tests pass. |
| Populated project | Share, incremental existing-copy join, exact scene equality, both session ends and unchanged original source pass. |
| Desktop collaboration | All 55 scenarios pass across the full run and corrected setup reruns. |
| Backend | 2,153 tests pass; 15,758/15,758 lines covered (100%). The gate and exclusions are unchanged. |
| Builds | Desktop, backend and optional Steam build pass; TypeScript checks pass. |
| Source checks | All eight repository checks pass. |

The full desktop run passed 53/55. The two corrected setups now pass: a returning
writer publishes through the supported collaboration API both before removal and
after readmission, and Board interactions wait for the joined workspace to finish
loading. The separate populated-project acceptance also passes on the optimized
relay implementation. Core and SDK production code are unchanged by this
correction; their earlier coverage results below remain the baseline.

Standards review found no blocking violations. Requirements review verified the
relay-only boundary, incremental transfer, snapshot catch-up, strict metadata
validation, revoked-device denial and certificate-preserving readmission; no
blocking findings remain. Current evidence is kept under
`app/out/collaboration-test-results/relay/` and in `app/out/relay-*.log`.

Aislop scores the correction at 100, with no errors or fixable warnings. Five
non-fixable advisories remain: four private host/join/transfer methods have seven
parameters, and the existing synchronous backend disposal bridge waits for async
cleanup. The method arguments describe distinct transport/lifetime inputs; the
disposal bridge serves the existing `IDisposable` entry point. Neither rule was
disabled. The builds also retain the pre-existing ImageSharp 2.1.11 advisories.

The server checkout and all desktop changes remain local. The VS Code
**Collaboration: Local server + 2 apps** configuration builds this relay without
PostgreSQL. The optional Steam build with App ID 420 compiles, but two-account
Steam runtime validation remains outstanding. Earlier evidence below describes
the superseded persistent-server implementation, not the current relay result.

## Previous persistent-hosting checkpoint

The [task graph](../specs/realtime-collaboration.md) is complete for the local
testing handoff. Final implementation checkpoint: `dd98251e`. The integration
includes whole-project synchronization, hosted resume and independent copies,
TCP sessions, the optional Steam adapter, named focus locks and scene presence,
shared scene/Manuscript/Codex/research prose, access management and encryption-key
rotation. Scene planning prose, location hierarchy guards and Board metadata
commands use the same collaboration boundaries.

All four final review corrections are implemented and exercised. Additional
regressions cover expired rename leases, deleted rename sources, Research storage
paths, imported rich markup and pending Manuscript edits across view switches.

| Validation | Result |
| --- | --- |
| Core | 3,947 tests; 22,050/22,050 lines covered (100%). |
| SDK | 61 tests; 30/30 lines covered (100%). |
| Backend | 2,163 tests; 15,877/15,877 lines covered (100%). |
| Collaboration client | 151 tests pass. |
| Local server and contracts | 143 tests pass. |
| Desktop transport/document host | 135 tests pass. |
| Real local-server integration | 26 tests pass against SignalR and PostgreSQL. |
| Desktop collaboration | All 54 scenarios pass across the combined run and targeted reruns. |

The combined desktop run passed 52/54 scenarios. Cursor initialization and
Research fixture synchronization were corrected; both affected scenarios pass
on rerun, along with the affected cursor and Manuscript checks. Coverage includes
real hosted project exchange, direct TCP, rich/Markdown editing, per-writer undo,
field locks, scene presence, metadata/assets, access rotation, reconnect,
deletion recovery and retained copies. TypeScript checks, desktop/backend builds
and the optional Steam build pass. The repository's eight source checks pass.
Coverage thresholds and exclusions were not weakened. Evidence is preserved in
`app/out/collaboration-test-results/final/`.

### Standards review

The independent review found no hard documented-standard violations and two P1
correctness/security findings. Both are resolved: live file batches validate
embedded storage references before projection, and interrupted renames retain
durable intent with lease checks through their prose updates. Guarded Research
metadata also validates storage paths before commit and projection.

### Spec review

The independent requirements review found a P1 Manuscript save-path mismatch and
P2 Board POV/custom-property commands bypassing leases. Both are resolved:
Manuscript binds its existing scene editors to shared documents, retains pending
edits and publishes focus-based presence; Board commands acquire field leases
and show named denials. Narration remains read-only playback. All four findings
across the two review axes are closed.

### Remaining validation limits and warnings

Native Steam playtesting still requires the registered app and two entitled
accounts; only compilation and the shared transport boundaries are verified.
The .NET build reports existing ImageSharp 2.1.11 vulnerability advisories.
Focused Aislop scans have no errors or fixable warnings. Remaining non-fixable
reports include the existing eight-parameter `StateOverrideDto` constructor and
server primary-constructor class-length false positives (`FieldLeaseAccess`,
`ProjectEpochAccess` and `UpdateLog`); the server's existing multi-server TODO is
also recorded in its local change manifest. These findings were not suppressed.

Hosted testing uses the extended server checkout under
`app/out/collaboration-sync-server` at `29358eb` (contracts 1.6.0). No fork, push,
upstream PR or deployment is authorized until the user tests the result and
explicitly approves publication.
The [local testing guide](../development/collaboration-testing.md) includes
separate desktop profiles and a disposable hosted-server helper. All temporary
implementation worktrees and their merged branches have been removed; the local
integration branch and server checkout remain available for testing.

## Historical checkpoints

The sections below record earlier implementation stages. Their test counts and
remaining-work statements describe those stages, not the current integration.
Use the task graph and current status above for outstanding work.

### Implemented and exercised

- Shared scene dialogs acquire focus locks for title, point of view and the
  story-date range. Close flushes pending edits; a save failure keeps the dialog
  open. Two real TCP desktops exercise named-lock handoff, remote values and
  clearing a range. Date-range writes acquire the separate Date lock before
  mirroring a nonempty start, matching local date semantics. New scenes can set
  their initial point of view before their shared field identities are created.
- Existing Codex image names, image descriptions, override image names and
  attachment names use the same named focus locks. Inherited override images
  become a separate scoped list on first focus. Structural file projection
  preserves separately synchronized field values while adding those targets;
  the hosted two-session regression retains an edited age while adding scoped
  image bindings.
- A Yjs binding extends the existing iframe editor. Browser checks cover
  concurrent text and formatting, paragraph splits, per-writer undo, IME,
  annotations, footnotes, images, nested lists, cursor positions, saved state,
  inert sanitization and rejection of invalid prose values. Pagination and
  grammar decorations stay local. The application now attaches this binding
  when scene reads return a shared identity. Electron checks cover pending
  edits across repeated editor remounts, retry after save failure, and loading
  the selected scene's annotations. Hosted create/join/end controls are now in Settings.
- `Novalist.Sync.Client` speaks the community server's existing SignalR /
  MessagePack protocol. It registers devices, creates and joins projects,
  pushes encrypted updates, and streams catch-up. Authentication tokens come
  from a caller-supplied provider. Desktop Settings now has browser sign-in,
  cancellation, remembered accounts, sign-out and hosted session controls.
- Hosted sign-in uses the native authorization-code flow with PKCE and an
  ephemeral loopback callback. ID tokens require verified signatures, issuer,
  an exact client audience, lifetime, nonce and subject. Refresh credentials
  persist through the private backend-to-main bridge using Electron safeStorage;
  unencrypted Linux storage is refused. Tests cover rotation, canceled callers,
  storage failures and retryable shutdown, account restoration, and two separate
  Electron launches. Access tokens are never returned to the renderer.
- Per-account device identities retain Ed25519 and X25519 private keys in that
  OS-encrypted store across restart. The verified transport registers immutable
  public keys and proves possession for every connection. A real local-server
  test covers two accounts, invitation redemption with the owner disconnected,
  and reconnect. Two additional live relay tests reject proofs forwarded through
  another server. Private invitation bootstrap now provides project-key onboarding.
- Epoch key wrapping implements the server design's §6.3 X25519/HKDF/
  XChaCha20-Poly1305/Ed25519 construction. Tests verify recipient and grantor
  binding, tampering, and the literal big-endian signature context. Membership
  checks and the epoch-rotation workflow remain to be integrated.
- Update envelopes use the server's v1 header format, HKDF-SHA-256,
  XChaCha20-Poly1305, and Ed25519 with the published domain strings. Signature,
  project and epoch validation precede plaintext delivery. Owner-authorized
  device certificates carried inside the encrypted payload authenticate writers.
  Membership revocation, device recovery and key rotation remain unfinished.
- SQLite journals retain exact encrypted envelopes for retries and plaintext
  document updates for local recovery. Device sequence allocation survives
  fully closing and starting sessions. Closed journals remain readable but
  cannot send or accept more updates. A contiguous server cursor prevents
  a live update from skipping missing catch-up records.
- Checkpoint creation and installation preserve selected project files,
  validate paths, sizes and hashes, reject links and duplicate paths, and
  publish into a new destination only after all files validate. Selection
  covers every book and draft, shared metadata, Codex, images and research;
  private app configuration, executable extensions and local snapshots stay
  out of transfers. A checkpoint containing multiple drafts opens through the
  real project service after installation. Version 2 also carries opaque shared
  document states with their item identities, file bindings and hashes. Corrupt
  state and quota failures cannot publish a partial project.
- Checkpoints travel as encrypted chunks through the unchanged Update API.
  The manifest is published last and pinned by its hash; download validates
  all chunks before installing the project. Integration covers a 1.1 MB binary
  attachment and downloading after the owner disconnects. The inner client
  payload has a versioned fragmentation header, rather than raw Yjs bytes.
- Framed TCP packets are implemented and tested over real loopback sockets,
  including fragmented and coalesced reads. Session hosting/handshake and a
  complete TCP transport adapter are still required.
- The background prose document host merges and projects unopened scenes and
  restores their Yjs identities. It uses jsdom with scripts and resource
  loading disabled by default, and shares the visible editor's sanitizer and
  markup code. The desktop build now includes an Electron utility process and
  a session-isolated bridge over the existing duplex backend connection. A .NET
  client, scene coordinator and backend read/write RPCs are wired. Shared
  edits are journaled before projection into ordinary scene files; malformed
  remote edits are quarantined by fingerprint so replay can continue. Recovery
  republishes withheld edits to open editors. Project close/open/create stops
  intake and projects journaled scene edits before sealing the old session;
  a failed local save leaves closure retryable. Word counts and scene-save
  notifications are updated for the active draft. Normal desktop window close
  and update installation also end the session before terminating the backend.
  Bookkeeping for inactive drafts remains unfinished. The desktop dependency
  is excluded for iOS RIDs.
- Shared scene projection updates the durable word-count baseline without
  crediting remote changes to the local writer's goal. Project close/reopen tests
  check that a subsequent local save counts only its own delta. Existing word
  history regression tests also pass.
- Deletion baselines retain edited prose separately while the scene stays
  deleted, including late updates and restart. This is exercised at the document
  API; journal coordination, recovery files and a recovery UI are still needed.
- Initial scene sharing checks a faithful sanitized HTML roundtrip before
  assigning an identity. Unsupported markup currently rejects sharing; it is
  not silently removed. Incomplete initial histories are rejected. Incremental
  updates waiting for dependencies remain outside accepted document state, and
  checkpoint capture explicitly fails until those dependencies arrive.
- Browser and Electron checks cover out-of-order edits, immediate worker
  shutdown, worker exit, session isolation and saved-state restoration. Review
  found and fixed pending-update validation and shutdown races. The utility
  process also handles fatal error events without logging diagnostic reports;
  that specific V8 fatal-event path has not been exercised successfully.

### Local server evidence

The user selected local deployment for validation. The unchanged community
server at commit `199201abad46dc451d2b7d19076cdd894146e26d` is checked out in
ignored `app/out/collaboration-sync-server`. Portable PostgreSQL 17.11 is under
ignored `app/out/collaboration-postgres`; its temporary cluster listens only
on loopback. No globally installed database or production authentication was
changed.

All 110 baseline upstream tests passed. The Novalist integration tests start the real
server with Kestrel, real PostgreSQL, and test-only JWT validation. They verify
encrypted live delivery, identical retries, access rejection, owner-offline
catch-up, and sending locally saved updates after reconnect. Contributor
membership is provisioned in the test database because the upstream API has
no invitation endpoint. This is a test fixture, not an implementation of
membership management. Additional tests now exercise real invitation APIs in
the focused server branch described below, without test-only membership setup.

### Necessary server additions

The separate `feature/collaboration-invitations-presence` branch in the local
server checkout adds authenticated, owner-created single-use invitations and
an ephemeral encrypted presence relay. Invitations expire, can be revoked,
serialize concurrent claims, allow retries by the claiming account, and cannot
restore removed membership. Revocation records a tombstone even when it arrives
before creation. Invited viewers become contributors on their first claim.
The focused local commits are `8acc48a` (invitations) and `ec45c64` (presence).

An additional uncommitted endpoint, `/client-configuration`, publishes only the
server's public native sign-in authority, client ID and scopes. Administrators
must configure their identity provider for a public PKCE client with loopback
callbacks; the endpoint returns 503 when those settings are absent. Five server
startup tests pass with this addition. It does not provision an identity provider.

Presence validates membership, active device, epoch and envelope sizes, and
never enters catch-up or consumes a server sequence. The client uses a separate
derived presence key and rejects presence packets as document updates.

The server patch passed `dotnet format`, a warning-free build and all 120 tests.
Independent standards/spec reviews found invitation races and role handling
issues, which were fixed and tested; their follow-up found no remaining blocker.
The API grants access only: verified device onboarding, key distribution and
rotation remain client integration work. No server private keys or plaintext content
were introduced, and no upstream PR has been published yet.

Further uncommitted server changes add immutable public device keys, signed
device linking and fresh connection challenges. Administrators configure the
canonical `Collaboration__ServerUrl`; proofs bind that endpoint, independently of
request Host headers. The first verified device opts the account out of legacy
UUID-only operations and closes its existing connections. Invitation operations
then use the verified hub. Account locks serialize this cutover with legacy
mutations and device revocation; a controlled interleaving test reproduced the
earlier race and passes with the fix. Public metadata and wire encodings are
documented in the server's design §10; Contracts is now local version 1.2.0.
No external deployment or upstream publication has occurred.

After the device-proof review fixes, all 13 Contracts tests and 113 server tests
pass, along with all nine Novalist transport integration tests. The server build
has zero warnings and `dotnet format` completes. Review found no remaining blocker
in that device-authentication slice. The focused upstream Aislop scan reports no
errors or fixable warnings; three non-fixable warnings remain for dependency
parameter counts and a primary-constructor class misidentified as a long function,
plus the upstream connection registry's existing multi-server TODO. These are
not a clean full-project scan or a final desktop validation result.

Focused checks include the original 25 client tests and six scene-coordinator
tests, six server integration tests, 20 editor browser tests, two application
editor persistence tests, seven background document tests, an Electron
process/bridge test, 11 desktop router tests, a full-duplex backend bridge test,
and three backend project-transition cases. Each has passed at its relevant
implementation step; this is not a final full-suite result. TypeScript
typecheck and desktop/backend builds pass. Latest scans of the new client,
editor binding and scene backend code report 100 with no findings. Tests referencing Core also report its
existing ImageSharp 2.1.11 dependency advisories; these were not suppressed.
The npm security audit could not complete because its registry endpoint timed
out; no clean dependency audit is claimed.

Run the client checks with:

```powershell
dotnet test tests/Novalist.Sync.Client.Tests/Novalist.Sync.Client.Tests.csproj
dotnet test tests/Novalist.Sync.IntegrationTests/Novalist.Sync.IntegrationTests.csproj
```

The integration project requires the pinned upstream source above and
`ConnectionStrings__Default` pointing to an isolated PostgreSQL instance whose
role can create databases. It creates and removes its own test database.
Override `SyncServerSource` if the upstream checkout is elsewhere. It is
deliberately separate from the ordinary solution: production/client builds do
not reference the AGPL server or require PostgreSQL. The client implements
the wire format independently and does not vendor server code.

### Desktop sharing validation

Settings now creates hosted projects, generates private invitations, joins into
a new local folder, displays connection state and ends sessions. Initial capture
includes all drafts without changing navigation. Scene source hashes reject
changes made during capture or network publication; the local writing is kept.
Incoming checkpoints validate metadata storage references and source-of-truth
scene/chapter identities before opening or migration. Both outgoing and incoming
settings use the same allowlist, excluding personal preferences and service
configuration. Custom prose fields and opaque research attachments are retained.

The owned hosted runtime retains journals across interruption and seals them on
explicit session end. Startup credential waits and create/join/invite operations
can be cancelled during backend shutdown. Regression coverage includes a stalled
desktop capture reply and a real PostgreSQL lock blocking invitation creation.

Latest complete runs pass 116 client tests and 21 community-server integration
tests. A real two-Electron-app test creates and joins through Settings, exchanges
edits through both existing editors and verifies retained local copies after
session end. This adds coverage beyond the document-host stand-ins used by the
backend RPC lifecycle tests. TypeScript typechecking and the desktop build pass.
These are focused collaboration results, not a final application-wide gate.

Certified, encrypted presence now reaches the existing editor in the real
two-window server test. Participant cursors expire after 15 seconds without an
update; local polling continues while publication is stalled. Independent sends
have a ten-second cancellation bound. Closed editor bindings cannot change a
later session's presence status, and backward selections display the caret at
their focus endpoint. The focused browser run passes 23 editor/presence tests;
the checkpoint import suite passes 24 cases, including entity files named
`scenes.json` that must not be mistaken for scene manifests.

The accepted Codex metadata policy is exclusive editing of each non-prose field
while a writer has focus. Release follows saving and moving focus elsewhere;
disconnects must expire abandoned locks. Acquisition must be authoritative so
simultaneous focus cannot grant two writers the same field. Offline editing
remains limited to synchronized prose. The local server now grants exclusive
30-second field leases, accepts guarded encrypted writes atomically, and retains
retired focus IDs so delayed requests cannot revive a lock. Acquisition returns a
catch-up sequence. Eight PostgreSQL service regressions and two open-connection
revocation cases cover contention, expiry, focus handoff, delayed blur/acquisition
and lost-response retries. A client transport test exercises real authenticated
connections and encrypted edits.

Guarded field saves now persist separately from the ordinary reconnect queue.
Unconfirmed values survive restart and session closure for recovery; they cannot
silently upload without their original lease. Confirmed field history waits for
sequence gaps and complete messages. Codex scalar controls now acquire before
editing, renew while focused, and save before releasing. A checkpoint v3 catalog
binds each existing field to its own opaque item identity and local entity file;
projection updates that field in its original book even after navigation.
Both ordinary update key spellings respect the lease guard, and settled entries
remain protected. A real two-app test exercises contention and immediate focus
handoff through the community server. The desktop control regression also covers
failed-draft retention across remount and explicit local recovery copies, including
a failed recovery write. World Bible history is stored project-wide and can be
listed/restored after sharing ends; legacy active-book revisions remain listed.

Still missing from the Codex path: synchronized creation/deletion and structured
properties, prose fields, name-change cascades through shared references and
prose, a recovery browser for pending journal edits after restart, and protection
across all alternative mutation paths. The renderer preserves unsent field drafts
in memory until a guarded save or explicit recovery copy reaches the backend;
an abrupt renderer crash before that point does not yet persist them.

To run the real desktop/server test after building the app and backend, with the
local fixture paths described above:

```powershell
cd app
$env:NOVALIST_SYNC_INTEGRATION = '1'
npx playwright test collaboration-live.spec.ts
```

It starts a disposable local server and test identity provider, creates/migrates
an isolated PostgreSQL database, and removes that database on teardown. Without
the environment flag this deployment-dependent test is explicitly skipped.

### Work still required at the earlier checkpoint

The latest client-library slice now creates private invitation links, persists
joining keys and certificates before claiming, and verifies owner-authorized
device certificates carried inside encrypted updates. The local integration
test restores joining credentials, downloads an encrypted checkpoint while the
owner is offline, then delivers the guest's edits after the owner reconnects.
Tampered signatures, changed authority after restart, future-dated grants, and
device-ID substitution have focused regressions. Device IDs bind both public
keys; earlier unreleased random-ID credential files are rejected. The server's
new activation receipt supports the same account/device/certificate retry after
expiry, without restoring removed membership or revoked devices. Desktop sharing
now uses this flow; epoch rotation is not implemented.
The pending-invitation replacement and post-claim credential-save failure cases
also pass against the real local server. A live synchronizer now sends journaled
edits automatically, reconnects after transport loss, and stops when a session is
paused, closed or disposed. Its integration test observes delivery after a
disconnect without manually requesting synchronization. A regression protects
the failed status when a bad live signature arrives during initial catch-up.
Initial offline startup still needs integration. The upstream formatted build
has zero warnings, and all 13 contract
and 125 server tests pass. These are not a final whole-application verification.

- Whole-project live coordination: Codex, structure and asset merging. Complete
  support for markup rejected by the initial importer.
- All existing mutation paths, including external files, AI, extensions,
  snapshots and independent participant navigation.
- Recovery-file/UI integration, independent copies, and desktop reconnect lifecycle.
- Access management, key rotation and the participant list. Account sign-in and OS-encrypted
  credential/device storage are implemented, but device linking/recovery still
  needs its desktop workflow.
- TCP session hosting and Steam adapters. Actual Steam validation depends on
  the SDK, runtime, AppID and accounts; none has been validated here.
- Full application regression suite, final review, and implementation commits.

Hosted Settings controls and their Unreleased entry are present on the branch.
No upstream pull request
has been published; server modifications remain in their separate local checkout.
