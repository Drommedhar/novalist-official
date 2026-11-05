# Dockable Scene Notes Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement these tasks inline.

**Goal:** Make scene notes freely dockable in the existing layout and responsive to their size; align the title and edit control.

**Architecture:** Add an auxiliary notes pane to the existing pane tree. Native notes state coordinates floating/docked ownership, visibility and follow mode. Share checklist rendering between presentations.

**Tech Stack:** React, TypeScript, Zustand, Electron, existing CSS tokens.

**Spec:** docs/superpowers/specs/2026-11-05-dockable-scene-notes-design.md

## Constraints and review focus

No new dependencies; preserve notes text, pending saves and shared editing. No new top-level view. Check nested placement, repeated movement, failed transfer, saved layouts, small panels and focus mode. User authorized relevant local Electron E2E for this run.

### Task 1: Pane placement and safe native transfer

- [x] Add failing Node tests for placement at all four edges, moving without duplication and native transfer acknowledgement/failure.
- [x] Add PaneView, pane docking helpers, native docking state and preload APIs.
- [x] Run focused Node tests and confirm they pass.

### Task 2: Notes integration and responsive layout

- [x] Add placement controls to existing pane rendering and a shared docked notes surface; preserve active editor and follow/hold context.
- [x] Center heading controls; adapt roots to horizontal cards and editor below in wide panels.
- [x] Add E2E assertions for docking, moving, saved layouts, floating again, persistence and geometry.

### Task 3: Verify and document

- [x] Update locales, manual, README and existing Unreleased feature entry.
- [x] Run typecheck, focused Node tests, build and doctors.
- [x] Run relevant Electron E2E with the user's explicit consent.
- [x] Review the complete diff and resolve substantive findings.

## Execution record

Working in the existing clean dev checkout so changes remain in the user's workspace. No commit or publication requested.


Completed verification: 53 focused Node tests and 9 Electron E2E tests pass. Typecheck, production build, renderer locale doctor (existing unused keys reported), token doctor, RPC doctor and diff whitespace checks pass. Independent review findings were resolved and rechecked: detached docking routes to main, asynchronous layout changes retain a destination, and hide/show preserves manuscript iframe identity and immediately typed text. The E2E regression was observed failing before the DOM-preservation fix and passing afterward. Focus Mode notes access also passes.

Implementation note: importing the complete save barrier into the docking coordinator exposed a browser timer typed as a Node timer under the E2E TypeScript project. The existing mobile idle-backup timer now explicitly uses the browser's numeric handle; its two regression tests pass.


Restart follow-up: user testing found that named layouts preserved docking but the current placement was not restored automatically. Added a validated machine-local pane snapshot with fresh runtime pane IDs and native docked/visibility preferences restored before the first state request. Pane resize, movement, hiding and explicit floating now survive full app restarts without saving a named layout. The new full-restart E2E reproduced the original failure and passes after the fix, including hidden-note restoration and returning to floating. Follow-up verification: 58 Node tests, 9 notes/layout/restart Electron E2E tests, typecheck, build and repository doctors pass.
