# Scene stage transition implementation plan

> Execute inline using superpowers:executing-plans. The user approved this compatibility approach in the conversation.

**Spec:** `docs/superpowers/specs/2026-11-03-scene-stage-only-design.md`

**Goal:** Use scene stages throughout normal writing workflows while preserving legacy chapter-status API and queries.

**Architecture:** Keep backend contracts and persisted data unchanged. Remove chapter-status affordances in the renderer, retain an explicit legacy-filter indicator, and use existing scene-stage data for presentation.

## Tasks

- [x] Add regression coverage for mixed stages/chapter statuses, preserved legacy presets/Smart Lists/SDK, and configured stage colors. Run the focused tests before changing implementation.
- [x] Remove chapter controls, Manuscript badge and cycling; expose scene-stage filtering first, with a removable legacy restriction only for existing presets. Update card colors, Dashboard, and Smart List rule choices.
- [x] Document SDK deprecation without changing signatures or runtime behavior. Update manual, README, locales, and one consolidated Unreleased transition entry.
- [x] Verify targeted backend/Node tests, headless renderer checks, types, source checks, desktop/mobile builds, and perform an independent review.

## Review focus

- No chapter state is inferred or written from scene stages.
- Old SDK reads/writes, imported metadata, legacy query rules, and presets retain their semantics.
- Legacy restrictions are visible; new filters cannot accidentally mix chapter and scene stages.
- Default/custom/unset scene stages color correctly and remain editable.
- A filtered manuscript has the right scene set and retains pending edits when filtered.

## Execution record

Pre-flight: the renderer consumes unchanged RPC/SDK contracts. Existing scene-notes work remains uncommitted on the current branch; this task also remains uncommitted, with no release, push, or merge.

The user's project metadata confirms 2 first-draft scenes and 114 outline scenes, but 22 chapters carry unrelated legacy statuses (1 Revised, 12 FirstDraft, 9 Outline). Read only; no project mutation.

Task 1 complete: focused renderer regression suite RED (5 failures / 1 pass) then GREEN (6/6). Backend contracts include mixed scene stages under unrelated chapter statuses, legacy Smart Lists, and SDK read/write round trips.
Task 2 complete: 160/160 Node tests, 54/54 targeted backend tests, typecheck, all 8 source checks, desktop and mobile builds pass. Native Manuscript E2E updated and typechecked; not executed on the interactive desktop.
Task 3 complete: SDK members carry non-error Obsolete attributes per the user's follow-up, with compatibility and migration guidance. This preserves binaries and runtime behavior; recompilation emits warnings. Manuals/locales updated. Confirmed main's published 3.6.2 changelog stamp and preserved its released entries.
Execution adaptation: changes are intentionally uncommitted, including earlier Notes work. Final review uses the working-tree diff and untracked files, since the commit-range packaging helper rejects an empty range. No user project files changed.

Final review: independent read-only reviewer found two important filter defects; no critical or minor findings and no declined judgments.
Final: fixed pending prose being replaced during filter reloads — manuscript-filter-reload.test.mjs covers iframe capture, edits before/during reads and saves, an autosave completing during a stale read, failed writes, and overlapping filter requests. RED→GREEN; full Node suite 170/170.
Final: fixed empty Smart List results showing the full book — regression covers zero matches, clearing selection, Show the whole book, and failed query retention. RED→GREEN; backend empty-array compatibility remains unchanged.
Task 4 complete: Node 170/170; targeted backend 54/54; headless real Smart List add/edit/save and manuscript rendering/edit flush passed; typecheck passed; all 8 source checks passed; desktop and mobile builds passed; git diff --check clean. Desktop E2E remains updated/typechecked but unrun for this task, per desktop isolation rules. Logs: /private/tmp/novalist-scene-stage-{node,backend,headless,types,source,build,mobile}.log.
Completion: leave the current branch and uncommitted changes in place. No commit, push, merge, deployment, or user-project mutation performed.
