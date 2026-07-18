# UI rewrite parity audit

Status of the Electron + React shell (branch `electron-rewrite`, `app/` +
`Novalist.Backend/`) measured against the Avalonia application's feature
surface. Verification: 188 backend tests at 100% enforced line coverage,
three Playwright end-to-end suites driving the real app (including against a
real 119-scene project and the packaged build), plus the frozen Avalonia app
still building and gated in CI.

## Verified parity

- Projects and books: create, open, recents; book and draft switching and
  creation from the toolbar; on-disk format byte-compatible (same
  `.novalist/` control plane, `.novalist` scene files with identity
  comments, word counts written with the identical regex).
- Writing: the original editor.html verbatim (typewriter scroll, focus
  behavior, page view, auto-replacement, dialogue correction inside the
  page), 2-second autosave parity, formatting toolbar, split-second scene
  switching, comments and footnotes persisted to the manifest, grammar
  check round-trip (LanguageTool with credentials/picky/mother-tongue),
  scene snapshots with restore, focus mode (Alt+F), entity-mention
  highlighting with hover cards (names pushed from the codex), split
  editor (open a second scene side by side from the binder menu).
- Binder: chapter/scene tree with act headers, status dots with click
  cycling, rename/delete context menus, drag-and-drop chapter/scene
  reorder and cross-chapter scene moves, scene archiving with a
  restore browser, act assignment from the chapter menu, smart lists
  tab with rule editor and evaluation.
- Planning views: Dashboard (totals, goals with editing, streak/history
  chart, status breakdown, pacing, echo phrases — algorithm ported
  verbatim), Manuscript (manuscript-editor.html verbatim plus native
  corkboard and outliner with synopsis/POV editing), Timeline (acts,
  dated chapters/scenes, manual events with editor, zoom/mode persisted),
  Plot Grid (plotline CRUD, cell toggling), Calendar (week/month/year via
  StoryDateResolver, drag-to-reschedule), Relationships (family-cluster layout from
  locale-driven role keywords, search/world-bible filters).
- Codex: four entity types plus user-defined custom entity types (full
  CRUD, tabs, and a type manager for fields/features), list with
  portraits, scalar field editing, aliases, sections, relationships,
  image management (gallery pick and file import), typed custom
  properties resolved from templates, per-chapter/scene character
  overrides with diff storage (blank = inherit, matching the Avalonia
  null-field model), guided-creation wizards for every entity type and
  the seven-pillar character interview, and the full template editor
  (known/custom fields, typed property defaults, sections, age mode)
  covering built-in and custom types with create-from-template.
- Timeline extras: story-structure templates (Three-Act, Save the Cat,
  Hero's Journey, 7-Point) applied as manual-event beats, plus
  character/location/source filters and event chips.
- Obsidian plugin-vault import: detection (plugin data or folder
  heuristics), project selection, full conversion with import log, and
  the relationship-pair/auto-replacement settings merge.
- Research and gallery; Export in all seven formats (files verified
  non-empty per format in tests); Git (status, commit, push/pull,
  discard); Find/replace across scenes with snapshot-guarded replace.
- Settings: appearance/editor/writing-assistance/diagnostics with
  per-project override scopes and live propagation (language, theme,
  editor settings).
- Theming: light/dark following the OS plus migrated Discord and
  Catppuccin Mocha palettes; accent color override; native Liquid Glass
  on macOS 26+ (verified on hardware) with vibrancy fallback.
- Hotkeys (Avalonia gesture grammar) and command palette.
- Maps: 2D/3D map.html + three.js stack verbatim, load/save/rename/
  delete, image origin via the project protocol (no local HTTP server).
- Extensions: the .NET extension host runs headless in the backend with
  the complete Desktop test suite ported; SDK v2 additive webview
  contributions; the AiAssistant's three views (AI Chat, Character Chat,
  Story Analysis) ported as webviews wrapping the original ViewModels,
  loading from the deployed extension end to end.
- Packaging: electron-builder DMG with the self-contained backend
  bundled; the packaged app verified booting by e2e; update checks via
  electron-updater with an in-app banner (auto-download off on the
  unsigned macOS build). CI runs the backend coverage gate and the web
  e2e suite.

## Remaining gaps (not yet at parity)

- Manual (docs/manual/) still documents the Avalonia shell; needs the
  page-by-page rewrite once the new shell becomes the released app.
- Avalonia retirement (M9): the release pipeline still ships the
  Avalonia app; switching is a release decision, not a feature gap.

## Known simplifications (functionally equivalent, noted for honesty)

- Wizard drafts are not persisted to disk for resume-later (the desktop
  dialog saved partial answers under the book's Wizards folder).
- Import progress is shown as a busy state, not per-step text (the
  conversion itself, log, and settings merge are identical).

## Verification commands

- Backend: `dotnet test tests/Novalist.Backend.Tests --settings tests/coverlet.runsettings`
- App: `cd app && npm run typecheck && npm run build && npx playwright test`
- Package: `cd app && npm run backend:publish --rid=osx-arm64 && npx electron-builder --mac dmg`
