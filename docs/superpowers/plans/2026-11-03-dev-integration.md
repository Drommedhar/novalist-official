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

- [ ] Merge origin/feat/android-mobile; reconcile backend context reads, renderer context/Inspector, editor pending writes and desktop release notes.
- [ ] Merge origin/feature/realtime-collaboration; reconcile shared documents and metadata locks with current scene notes and manuscript workflows, Android storage, package/build configuration, and test harnesses.
- [ ] Validate combined code with source checks, Node tests, TypeScript checks, desktop/mobile renderer builds, and relevant .NET suites. Add focused regressions for integration defects. Inspect available native build prerequisites and report any unavailable validation.
- [ ] Independently review integration resolutions, address material regressions, verify both feature tips and main are ancestors of dev, commit and push dev.

## Review focus

- Shared scene notes must use collaboration documents and honor access/lock rules even in the separate Notes window.
- Manuscript reloads must preserve edits and remain functional with shared prose.
- Android project providers and collaboration file watching/storage must compose.
- Latest desktop release fixes, SDK deprecation annotations, and native shutdown guards must survive.
