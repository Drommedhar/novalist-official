# Realtime collaboration integration

Integration branch: `feature/realtime-collaboration`, based on main.
Requirements: the user's accepted decisions in
the acceptance criteria below and [ADR 0007](../adr/0007-relay-only-collaboration.md).
ADR 0007 supersedes earlier owner-offline hosting decisions in ADRs 0003–0004
and the feasibility assessment.
Existing implementation evidence: `docs/research/collaboration-implementation.md`.
No external issue tracker is configured; this file records the task graph.

## Acceptance

Two desktop instances join a populated project through the local community server.
Both can create, change, move and delete project content, see the results in the
other instance, reopen the shared project, and retain usable local copies after
ending. Covers all books/drafts, scenes, Codex, research, project metadata and
binary assets. Existing prose editing and cursors remain in the existing editor.
Non-prose editing is exclusive while a field has focus, with the editing writer
named in the lock message. Scene rows show presence badges with writer names in
the binder, outliner and corkboard, including scenes the local writer is not
viewing. Disconnected editing is
limited to synchronized prose. Deleted content stays deleted and concurrent edits
are recoverable. The server relays packets between connected clients and persists
no project files, snapshots or edit history. Joining requires an online host.
Clients compare project-relative paths and SHA-256 hashes and transfer only
missing or changed files, reusing an existing local copy when available. Scanning,
comparison and transfer show visible progress, including file and byte counts.
Steam and open-source clients share the relay mode; TCP and Steam peer sessions
use the same project model. The host controls session access. Relay administrators
configure resource limits, and each client tunnel bounds active and connecting
relay routes to 128. Ending and starting sessions has no handover.

Tasks C9–C11 track this correction. Previous completion evidence for C1–C8
describes the earlier architecture; it does not establish that relay-only
collaboration or incremental joining is complete.

## Task graph

| ID | State | Depends on | Deliverable |
| --- | --- | --- | --- |
| C1 | Complete | Existing foundation | Live project file/metadata protocol: creation, merge, moves/deletion, binary data, catch-up and local recovery. Test at the session boundary. |
| C2 | Complete | Existing foundation | Desktop reopen/resume and independent local copy, with durable context and no replay across fully closed sessions. |
| C3 | Complete | Existing foundation | Functional TCP peer host/client and optional Steam transport over the same session APIs; clear runtime availability. Native Steam acceptance awaits the registered app and two accounts. |
| C4 | Complete | C1 | Scene/Manuscript/Codex/research prose and metadata, per-writer undo, pending-edit retention and disconnected-write guards are integrated. Board POV/custom-property commands respect named field locks. |
| C5 | Complete | C1, C2, C4 | Real two-app structure, Codex, research/assets, move/deletion recovery, retained copies and reopen pass. Named field locks and different-scene presence badges pass hosted/TCP acceptance; all 54 desktop scenarios pass across the combined run and targeted corrections. |
| C6 | Complete | C3, C4 | Desktop peer session controls, initial project exchange and local TCP acceptance pass. Optional Steam adapter compiles; native runtime validation requires app registration. |
| C7 | Complete | C4 | Owner access management, removal with encryption-key rotation, remaining-writer refresh, safe re-invitation and project deletion pass three-app acceptance. |
| C8 | Complete | C5, C6, C7 | All four final review corrections and their regressions are complete. Core, SDK and Backend meet the unchanged 100% coverage gate; final application checks pass and temporary implementation worktrees are removed. Server changes stay local; publishing awaits the user's own testing and explicit approval. |
| C9 | Complete | Existing peer collaboration | Relay mode registers no database or storage endpoints and forwards packets only. Joining requires the online host; real relay transport and desktop acceptance pass. |
| C10 | Complete | C9 | Relative paths and SHA-256 select missing/changed files; matching files are reused and local differences are recoverable. Existing-copy acceptance covers large assets, private files and all 117 scenes of the populated project. |
| C11 | Complete | C9, C10 | Scanning/comparison/transfer progress, file/byte/reuse counts and cancellation work while the workspace is busy. All 55 desktop scenarios pass across the full run and corrected setup reruns; populated-project share/join/end passes and Backend retains 100% coverage. |
| C12 | Complete | C11 | Echoed typing preserves pagination; nearby Manuscript editors attach incrementally and retain undo. The copied 116-scene project loads and remains editable; offscreen remote edits and normal window close pass. |
| C13 | Complete | C11 | All 431 included host files remain byte-identical after sharing a populated copy. Join, legacy section edits and reordering pass; Core remains at 100% coverage. |
| C14 | Complete | C11 | Codex list, table and detail show named viewing/editing presence. Styled hosting/joining controls, a default new-folder name, destination feedback and centered progress pass real desktop checks. |
| C15 | Complete | C11 | Stable local helper addresses survive restart. Sign-in and relay failures occur before preparation; rejected accounts offer fresh sign-in, temporary outages retain credentials and unavailable accounts can be forgotten. Four desktop account scenarios pass. |
| C16 | Complete | C12, C13, C14, C15 | Populated two-window relay acceptance passes through Settings, new-folder joining, stable typing, all 116 active Manuscript scenes and normal close of both windows. All 69 desktop scenarios have passing evidence; Backend 2,160 tests and Core 3,949 tests retain 100% coverage. Standards/spec reviews have zero findings. Evidence is archived, temporary implementation branches/worktrees are removed, and all changes remain local. |

Approved test boundaries: editor API, collaboration session API, real transports.
Implementation agents use isolated worktrees from the integration branch;
completed changes return through a merger agent. Do not substitute unit-test
counts for two-app acceptance. Do not mark the feature complete while required
work remains in this graph.

The user requires all changes to stay local until they personally test the result
and explicitly approve publishing. Do not push branches, create a fork or open
the upstream server PR before that approval.
