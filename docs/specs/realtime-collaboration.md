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
Non-prose editing is exclusive while a field has focus. Disconnected editing is
limited to synchronized prose. Deleted content stays deleted and concurrent edits
are recoverable. Owner-offline availability and first joining remain supported.
Steam and open-source clients share the hosted mode; TCP and Steam peer sessions
use the same project model. Only the owner manages membership/deletes the hosted
project. No hard participant cap. Ending and starting sessions has no handover.

## Task graph

| ID | State | Depends on | Deliverable |
| --- | --- | --- | --- |
| C1 | In progress | Existing foundation | Live project file/metadata protocol: creation, merge, moves/deletion, binary data, catch-up and local recovery. Test at the session boundary. |
| C2 | In progress | Existing foundation | Desktop reopen/resume and independent local copy, with durable context and no replay across fully closed sessions. |
| C3 | In progress | Existing foundation | Functional TCP peer host/client and optional Steam transport over the same session APIs; clear runtime availability. |
| C4 | Pending | C1 | Connect the ordinary workspace write path, dynamic scene/Codex bindings, remote refresh and focused-field guards. |
| C5 | Pending | C1, C2, C4 | Real two-app acceptance for structure, Codex, research/assets, reconnect/reopen, deletion recovery and retained copies. |
| C6 | Pending | C3, C4 | Desktop peer session controls, initial project exchange and local TCP acceptance; Steam SDK/runtime validation when available. |
| C7 | Pending | C4 | Owner access management and revoked participant behavior. |
| C8 | Pending | C5, C6, C7 | Final application checks, standards/spec review, focused upstream server PR, implementation commits and cleanup. |

Approved test boundaries: editor API, collaboration session API, real transports.
Implementation agents use isolated worktrees from the integration branch;
completed changes return through a merger agent. Do not substitute unit-test
counts for two-app acceptance. Do not mark the feature complete while required
work remains in this graph.
