# UI rewrite parity audit

Status of the Electron + React shell (branch `electron-rewrite`, `app/` +
`Novalist.Backend/`) measured against the Avalonia application's feature
surface. Verification: 165 backend tests at 100% enforced line coverage,
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
  scene snapshots with restore, focus mode (Alt+F).
- Binder: chapter/scene tree with act headers, status dots with click
  cycling, rename/delete context menus, drag-free reorder still pending
  (see gaps), smart lists tab with rule editor and evaluation.
- Planning views: Dashboard (totals, goals with editing, streak/history
  chart, status breakdown, pacing, echo phrases — algorithm ported
  verbatim), Manuscript (manuscript-editor.html verbatim plus native
  corkboard and outliner with synopsis/POV editing), Timeline (acts,
  dated chapters/scenes, manual events with editor, zoom/mode persisted),
  Plot Grid (plotline CRUD, cell toggling), Calendar (week/month/year via
  StoryDateResolver), Relationships (family-cluster layout from
  locale-driven role keywords, search/world-bible filters).
- Codex: four entity types, list with portraits, scalar field editing,
  aliases, sections, relationships, image management (gallery pick and
  file import), typed custom properties resolved from templates.
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
  bundled; the packaged app verified booting by e2e. CI runs the backend
  coverage gate and the web e2e suite.

## Remaining gaps (not yet at parity)

- Binder drag-and-drop reorder of chapters/scenes (reorder RPCs exist in
  Core; UI pending) and scene move between chapters.
- Character per-act/chapter/scene override editing UI (data preserved and
  round-tripped; no dedicated editor yet).
- Entity creation templates/wizard flow, custom entity type manager,
  template editor, custom entity types in the codex tabs.
- Archived scenes browser; acts management (create/rename act as an
  operation, headers render already).
- Split editor (two scenes side by side); book preview page mode toggle
  from the toolbar (page view works via settings).
- Context sidebar entity mentions/hover cards from the editor
  (entityHover events are emitted by editor.html; not yet surfaced).
- Timeline structure templates, outline export, character/location
  filters; calendar drag rescheduling (reschedule RPC exists).
- Import plugin flow, project rename UI, update notifications
  (electron-updater configured for Win/Linux; notify flow pending).
- Manual (docs/manual/) still documents the Avalonia shell; needs the
  page-by-page rewrite once the new shell becomes the released app.
- Avalonia retirement (M9): blocked on the gaps above by design.

## Verification commands

- Backend: `dotnet test tests/Novalist.Backend.Tests --settings tests/coverlet.runsettings`
- App: `cd app && npm run typecheck && npm run build && npx playwright test`
- Package: `cd app && npm run backend:publish --rid=osx-arm64 && npx electron-builder --mac dmg`
