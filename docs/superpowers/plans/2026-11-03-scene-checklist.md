# Floating Scene Checklist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make scene notes an automatically converted, expandable checklist in a movable desktop window.

**Architecture:** Preserve notes as text and derive a checklist tree from it. A singleton utility window joins existing workspace/save coordination and receives active scene context from workspace windows. Replace desktop duplicate note surfaces with the checklist and keep metadata in the inspector.

**Tech Stack:** React, TypeScript, Electron, existing Markdown editor and Node test runner.

**Spec:** `docs/superpowers/specs/2026-11-03-scene-checklist-design.md`

## Global Constraints

- No new dependencies or project schema. Use existing design tokens; no emoji.
- Localize in English, German and Simplified Chinese; update manual and README.
- Leave CHANGELOG.md unchanged until the running desktop release has stamped it.
- Use isolated runners for Electron E2E unless the user explicitly consents to the local desktop run; Node tests, typecheck, build and doctors are safe locally.

## Review Focus

- Mixed prose, fences, nested bullets, CRLF and existing completion survive conversion.
- A rapid scene change cannot write the previous scene's notes into the next scene.
- Failed/debounced edits survive hiding and block unsafe workspace/close transitions.
- Following scenes never leaks notes between projects, books or drafts.
- An off-screen saved window remains reachable after display changes.

### Task 1: Checklist model and persistence

**Files:** Create `app/src/renderer/src/shell/sceneChecklist.ts`, `sceneNotesDocument.ts`, and `app/scripts/scene-checklist.test.mjs`, `scene-notes-document.test.mjs`.

**Interfaces:** `normalizeChecklist(text: string): string`, `parseChecklist(text: string): ChecklistBlock[]`, `setChecklistDone(text: string, line: number, done: boolean): string`. Document session owns one scope, loaded text, pending-save registration and errors.

- [x] Add failing tests for Unicode/Markdown bullets, nesting, prose, fences, existing completion, line endings and idempotence; run `node --experimental-strip-types --test scripts/scene-checklist.test.mjs` from app.
- [x] Implement conversion/parser and line-targeted checkbox updates without rewriting unaffected text.
- [x] Test and implement document loading, serialized saves, failed retries and capture of the original scene identity.
- [x] Run the targeted Node tests and record results.

### Task 2: Floating window and scene context

**Files:** Create `app/src/shared/sceneNotesProtocol.ts`, `app/src/main/scene-notes-window.ts`, `app/src/renderer/src/shell/SceneNotesWindow.tsx`, `useSceneNotesContext.ts`; modify main/preload entry points, `env.d.ts`, renderer `main.tsx` and `DetachedPane.tsx`.

**Interfaces:** The preload exposes scene-context publication/subscription, notes-window toggle/hide and pin control. Scope includes project/book/draft/epoch, chapter and scene. Context updates are accepted from workspace windows only.

- [x] Add Node tests for bounds clamping, context ownership and singleton visibility behavior before implementation.
- [x] Implement the native notes window, saved bounds/pin, blur-hide, and workspace lifecycle integration.
- [x] Publish active scene context on scene changes and window activation, clearing it on workspace changes.
- [x] Render the editable checklist with nested details, progress, completed group, quick add, full-text editing and save-error retry.
- [x] Verify targeted Node tests and TypeScript.

### Task 3: Consolidation, documentation and verification

**Files:** Modify AppShell, FocusMode, Toolbar, SceneNotesFields, shellStore, inspector labels, locales, manual, README and E2E scene-notes tests. Remove unused desktop SceneNotesDock.

- [x] Route existing shortcut and visible Notes buttons to the floating window; retain scene metadata in the inspector and the existing mobile notes surface.
- [x] Add E2E assertions for nested checklist persistence, following/holding scenes, hide/restore and metadata reachability; run locally only with explicit consent.
- [x] Update docs and translations. Check published release state before editing Unreleased.
- [x] Run safe Node suite, typecheck, build, locale/token/RPC doctors and whitespace checks. Review the full diff and address correctness findings.

## Execution record

### Requested panel refinements

- [x] Reproduce global floating and absent native glass with controller tests, then replace global flags with active-workspace ownership and attach the glass view on load.
- [x] Use a frameless auxiliary window and a compact draggable header; remove the save-status footer and keep retry errors.
- [x] Update the manual, README, and desktop regressions for application ownership, hidden window controls, and the header's drag regions.
- [x] Complete safe validation and record desktop test scope for the refined panel.

Refinement verification: 154 Node tests, TypeScript projects, all eight source checks, and the desktop build passed. The four authorized Notes desktop cases passed with the new ownership and presentation assertions, including normal/fullscreen opening, transparent material backgrounds, edit/completion persistence, follow/hold, and auto-hide. Inspected the captured panel layout; native glass attachment is also covered by the controller regression. The published v3.6.2 stamp is incorporated and Unreleased contains one consolidated Added entry.

### Initial checklist and activation fix

Implemented on `codex/floating-scene-checklist`; changes are uncommitted for review.

- Checklist conversion, nested details, completion and persistence tests: passed.
- Independent review identified five correctness issues. Recovery session ownership, undo after an unacknowledged write, display minimum sizes, fenced-text hierarchy, and editor line endings each have regression coverage and are fixed.
- Verification: 152 Node tests, TypeScript projects, all eight source checks, desktop build and mobile renderer build passed. Nine relevant Electron tests listed successfully during initial implementation.
- After the user reported the main window disappearing and explicitly authorized desktop tests, reproduced a native `hide` event when Notes opened over a settled fullscreen editor. Disabled Electron's macOS process-type transformation when setting Notes workspace visibility. The new normal/fullscreen regressions pass, checking editor visibility, minimization, fullscreen, native events and page visibility through opening, editing and reopening Notes.
- Both checklist E2E cases also passed locally, covering bullet conversion, details, saved checkboxes, follow/hold, hide/restore and auto-hide. The auto-hide test now moves native BrowserWindow focus instead of only activating a CDP tab. Typecheck, five affected Node tests, desktop rebuild and all eight source checks passed after the fix.
- Notes has its persistent entry on the project toolbar, plus the shortcut and command palette. The duplicate View-menu entry was removed to comply with the repository's one-home placement policy.
- Release `v3.6.2` finished and the published changelog was checked before the deferred edit. Imported the release heading and comparison links while preserving the pre-existing local text edit, then added one consolidated Unreleased feature entry.

Prepared release-note wording: **Floating scene checklist** — keep scene notes in a movable window with automatic bullet checkboxes, expandable text and nested tasks, completion progress, scene following or holding, and pin or auto-hide controls. Scene properties remain together in the inspector.

### Restart persistence and shutdown follow-up

- Reproduced details collapsing on renderer reload in a native Electron test. Added scene-scoped local disclosure preferences for nested items and the Completed group, independent of transport epochs. Heading identities survive checkbox conversion and unrelated line insertion.
- Native regression exposed the queued browser toggle event losing an immediate expand/collapse before reload. Persist synchronously on summary activation, retaining native toggle handling, then verified a full application restart and separate scenes.
- Reproduced destroyed-window/WebContents access in native-controller teardown tests. Guard state publication, auto-hide, ready-to-show, glass attachment, and IPC controls during destruction. The user's exact one-off dialog was not reproduced in the normal native quit/close paths; it had appeared once while Notes showed saving.
- User explicitly authorized native tests for this run ("runn them its fine"). All 9 Notes Electron cases passed, including normal/fullscreen activation, full restart persistence, and quit/main-window close with delayed note saves in both pinned and auto-hide modes. Shutdown cases verify saved notes on disk and capture main-process exceptions.
- Final verification: 177 Node tests passed; TypeScript and all 8 source checks passed; desktop build passed. No user story files were changed, and all work remains uncommitted. Logs are under /private/tmp/novalist-notes-fixes-*.log.
