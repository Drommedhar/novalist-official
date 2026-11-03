# Android and Collaboration Integration Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. The user has authorized integration directly into dev and pushing dev; no further handoff approval is needed.

**Goal:** Combine the existing Android and collaboration branches with all current main/dev work on dev, retaining unfinished features.

**Architecture:** Merge the Android branch first, then collaboration. Resolve overlapping implementations by preserving their behavior together, with special attention to pending editor writes, shared document ownership, mobile storage, and the new floating Notes window. Keep published release history and the deprecated SDK chapter-status compatibility contract intact.

**Tech Stack:** Electron, React/TypeScript, .NET, MAUI, collaboration transport and shared documents.

**Spec:** User request in this session: bring both feature branches onto dev, including unfinished features. Current dev behavior must remain available.

## Global constraints

- Work on the existing dev branch; preserve remote feature branches and commit ancestry.
- Do not run native desktop E2E locally without fresh explicit consent for that run.
- Preserve published changelog sections; include only unshipped desktop outcomes in Unreleased.
- Retain scene-stage-only UI and SDK compatibility, Notes expansion persistence and shutdown protection, and pending Manuscript edit preservation.

## Tasks

- [x] Merge origin/feat/android-mobile; reconcile backend context reads, renderer context/Inspector, editor pending writes and desktop release notes.
- [x] Merge origin/feature/realtime-collaboration; reconcile shared documents and metadata locks with current scene notes and manuscript workflows, Android storage, package/build configuration, and test harnesses.
- [ ] Validate combined code with source checks, Node tests, TypeScript checks, desktop/mobile renderer builds, and relevant .NET suites. Add focused regressions for integration defects. Inspect available native build prerequisites and report any unavailable validation.
- [x] Independently review integration resolutions, address material regressions, verify both feature tips and main are ancestors of dev, commit and push dev.

## Review focus

- Shared scene notes must use collaboration documents and honor access/lock rules even in the separate Notes window.
- Manuscript reloads must preserve edits and remain functional with shared prose.
- Android project providers and collaboration file watching/storage must compose.
- Latest desktop release fixes, SDK deprecation annotations, and native shutdown guards must survive.

## Integration decisions and verification

- Android merge checkpoint: `ab7290f9`. All 355 Node tests and all TypeScript configurations passed at that checkpoint.
- Preserve provider batch reads, read caches, metadata caches and text-write semantics through the collaboration file wrapper.
- Shared Notes owns one live document across checklist/text modes, including offline edits and CRLF notes; shared prose bypasses external disk-refresh reconciliation.
- Manuscript filter reloads drain registered shared document saves before waiting for pending edits, preserving the local save/reload guards.
- Retain scene-stage-only UI and deprecated chapter-status SDK compatibility; preserve published changelog sections.
- Use temporary PowerShell and an ignored, pinned community-server fixture for complete local test coverage. Build Android on GitHub Actions; run desktop E2E and iOS simulator checks with the user's subsequent explicit authorization.
- Run the existing CI on `dev` as well as `main`, and run all Python tooling unit tests there.
- Independent integration review found no Critical/Important regressions; its 58 focused Node checks passed.
- Verified Core: 4,139 tests, no skips, 100% line coverage. SDK: 62 tests, no skips, 100% line coverage. Python tooling: 59 tests. Packaging: 9 tests. Desktop and mobile renderer builds and all source checks pass. Final combined Node, Backend and Sync runs follow the final integration edits.

All unit suites are now green: Core 4,139; Backend 2,391; SDK 62; Sync.Client 203; Node 403; packaging 9; Python 59. All have zero skips. Core, Backend and SDK retain 100% line coverage. The separate Sync.Integration suite also passes 46/46 against a temporary local PostgreSQL instance.

Both merges are pushed to `dev` at `7da06501`; current main and both feature tips are ancestors. Android development APK builds pass for x64 and arm64 in Actions run `41723736828`. The iOS 27 simulator build and all three collaboration smoke scenarios pass: TCP guest, hosted guest, and native host with desktop edits persisted to native storage. Temporary native test processes and the task-created simulator were cleaned up.

Desktop native validation exposed obsolete startup indicators and missing isolation in legacy test fixtures. All 69 direct Electron launches now explicitly use test-local Chromium profiles; startup waits use the backend handshake. A failed launch terminates its owned process before returning the original error. Nine new headless checks pass, as do all TypeScript configurations. The automatic browser sign-in collaboration case passes on retry. A complete desktop run follows these fixture fixes.
