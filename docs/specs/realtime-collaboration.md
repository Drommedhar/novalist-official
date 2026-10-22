# Realtime collaboration integration

Integration branch: `feature/realtime-collaboration`, based on main.
Requirements: the user's accepted decisions in
`docs/research/realtime-collaboration-feasibility.md` and ADRs 0003–0006.
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
are recoverable. Owner-offline availability and first joining remain supported.
Steam and open-source clients share the hosted mode; TCP and Steam peer sessions
use the same project model. Only the owner manages membership/deletes the hosted
project. No hard participant cap. Ending and starting sessions has no handover.

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

Approved test boundaries: editor API, collaboration session API, real transports.
Implementation agents use isolated worktrees from the integration branch;
completed changes return through a merger agent. Do not substitute unit-test
counts for two-app acceptance. Do not mark the feature complete while required
work remains in this graph.

The user requires all changes to stay local until they personally test the result
and explicitly approve publishing. Do not push branches, create a fork or open
the upstream server PR before that approval.
