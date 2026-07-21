# Novalist Mobile — Feature Adaptation Plan

Living map of every Novalist feature and how it should behave on mobile (iOS first).
Companion to `docs/mobile-port-plan.md` (architecture) and `docs/mobile-phase0-runbook.md`
(what's built). Reprioritize freely — this is the backlog we execute top-down.

## Legend

**Treatment** — `Keep` works in the WebView as-is · `Adapt` needs touch/responsive tweaks ·
`Redesign` needs a new mobile interaction (sheet/gesture/native) · `Defer` out of v1, revisit ·
`Cut` not applicable on mobile.

**Priority** — `P0` required for a usable v1 writing app · `P1` important, right after ·
`P2` later · `P3` someday / revisit.

## Where we are (Phases 0-4, on branch `mobile-port`)

- MAUI iOS shell hosts the real renderer over the in-process backend; native Liquid Glass
  tab bar (Dashboard / Write / Codex / Search / More); single-pane layout.
- Storage: real external folders via the iOS document picker + security-scoped bookmarks
  (open a Git repo folder cloned by an external client). Git ops: stubbed unavailable.
- Done: editor zoom-trap fix, touch chapter/scene CRUD (+dialogs), settings-load-at-boot,
  git UI hidden, native tab-bar localization (web pushes localized titles, re-push on
  language change; verified de on the simulator), portrait+landscape (safe-area insets on
  all sides + a native-measured tab-bar bottom inset that tracks rotation), mobile settings
  filter (hides Hotkeys / Updates+Integrations / Extensions / Watch-filesystem / log-folder
  reveal; keeps Appearance, Editor, Writing Goals, Writing Assistance, Templates, Diagnostics),
  external-folder picker + security-scoped bookmarks, and the writing-hub bottom sheet
  (Inspector button in the editor raises Context / Footnotes / Scene Notes tabs; the native
  tab bar hides while it is up).

---

## Decisions (settled)

1. **Context / Inspector sidebar → bottom sheet.** An "Inspector" button in the editor raises a
   swipe-up sheet holding Context + Footnotes; Scene Notes uses the same sheet mechanism.
2. **Defer all five heavy views** (Timeline, Plot Grid, Calendar, Relationships, Maps) for v1.
   Maps 3D stays P3.
3. **No native Git in the app.** The user manages the repo with an external iOS Git client
   (e.g. Working Copy) that clones/pulls/pushes to GitHub; Novalist just **opens that folder** and
   reads/writes files. So: no libgit2, Git view stays hidden. This makes **external-folder access
   (#4) the enabler for the GitHub workflow** — highest-value item. (Optional later: a read-only
   "N files changed" indicator; not required.)
4. **Real external folders.** iOS document picker → security-scoped URL + persisted bookmark
   (wire `beginProjectAccess`/`endProjectAccess`); also fixes recents-after-reinstall. This is how
   users open their externally-cloned Git project folders.
5. **Extensions/webviews: Defer** (App Store remote-code rules).
6. **iPad multi-pane: Defer** to after the iPhone core.

---

## 1. Dedicated views

| View | Treatment | Prio | Mobile notes |
|---|---|---|---|
| write / Editor | Keep | P0 | Core. Zoom-trap fixed. Selection toolbar + context menu need touch (§3). |
| dashboard | Adapt | P0 | Single-pane already; verify all cards reflow. Mostly done. |
| manuscript (continuous / corkboard / outliner) | Adapt / Redesign | P1 | Currently the "Write" tab shows Binder→editor, NOT this view. Continuous-read = Keep; corkboard = touch cards; outliner table = hard on phone. Decide if reachable in v1. |
| codex (world bible) | Adapt | P1 | Data-dense but navigable; it's a tab already. Detail fields, images, custom props, wizard — verify touch. |
| timeline | Defer | P2 | Heavy horizontal data view. |
| plotGrid | Defer | P2 | Heavy grid; poor fit for phone width. |
| calendar | Defer | P2 | Heavy custom grid + drag. |
| relationships | Defer | P2 | SVG force-directed graph; heavy. |
| maps (2D + 3D Three.js/WebGPU) | Defer → Cut(3D) | P3 | Biggest perf/AOT risk. Maybe 2D-only much later. |
| research | Adapt | P2 | Notes + links; needs open-external (have it) + import (picker). |
| gallery | Adapt | P2 | Image grid; copy/open actions. |
| export | Adapt | P2 | Route output to iOS Share sheet instead of a save path. |
| git | Cut | — | External Git app (Working Copy, etc.) manages the repo; Novalist just opens the folder (see external storage). View stays hidden. |
| extensions + ext webviews | Defer | P3 | App Store remote-code concerns. |
| settings | Adapt | P0 | Filter to mobile-relevant sections (§5). |

## 2. Writing-support panels / docks / popups

| Feature | Treatment | Prio | Mobile notes |
|---|---|---|---|
| Binder (chapters/scenes tree) | Adapt | P0 | Done: full-width + / ⋯ buttons + dialogs. **TODO: move/reorder** (drag fails on touch → up/down in ⋯ menu). |
| Inspector / Context sidebar | Redesign | P1 | See decision #1. Bottom sheet recommended; houses Context + Footnotes tabs. |
| Footnotes tab | Redesign | P1 | Inside the Inspector sheet. |
| Scene Notes dock | Redesign | P1 | Bottom sheet (Synopsis + Notes), or a segment of the Inspector sheet. |
| Focus Peek card | Adapt | P2 | Hover→tap: tap an entity → peek as a popover/sheet with the map-pin deep link. |
| Focus mode | Adapt | P2 | Hide the tab bar for distraction-free writing; a toggle in the editor. |
| Comment gutter | Adapt | P1 | Margin cards don't fit phone width → inline highlights + a comments sheet/list. |
| Split editors | Cut (phone) / Defer (iPad) | P3 | No room on a phone. |
| Scene tab strip | Adapt | P2 | Simplify; the mobile Write flow is one scene at a time + Back. |
| Smart Lists | Adapt | P2 | Works; verify touch. |

## 3. The editor (contenteditable in the WebView)

| Capability | Treatment | Prio | Mobile notes |
|---|---|---|---|
| Bold/Italic/Underline, alignments | Keep | P0 | Toolbar exists; enlarge touch targets. |
| Floating selection toolbar | Adapt | P1 | Position above the iOS selection handles; touch sizing. |
| Add comment / footnote | Adapt | P1 | From selection toolbar / long-press menu. |
| Grammar / LanguageTool | Keep | P1 | Works; the bottom-right status could move (→ your **Dynamic Island** idea: a "writing session" Live Activity with issue count). |
| Auto-replace + dialogue correction | Keep | P1 | Pure logic; works. |
| @-mention picker | Adapt | P1 | Touch-size the popup; keyboard interplay. |
| Page view / typewriter scroll | Keep | P2 | Works; verify with the on-screen keyboard. |
| Custom context menu | Redesign | P1 | Right-click → **long-press** action menu. |
| Ctrl+wheel zoom | Cut | P2 | No wheel (pinch disabled). Use Settings font size instead. |
| Autosave round-trip | Keep | P0 | Already works over the bridge. |

## 4. Dialogs / overlays

| Dialog | Treatment | Prio | Notes |
|---|---|---|---|
| CreateProject / Chapter / Scene / StoryDate / Input / Confirm | Keep/Adapt | P0/P1 | Work already (used on mobile). Size for phone + keyboard. |
| Snapshots (+ compare) | Adapt | P1 | The mobile "versioning" if Git is deferred. |
| FindReplace | Adapt | P1 | Wired to the Search tab; verify scope UI on phone. |
| ImportPlugin | Adapt | P2 | Needs the real folder picker. |
| HelpOverlay (manual) | Keep | P2 | Bundled; works. |
| CommandPalette | Defer | P3 | No keyboard on phone; keep for iPad + external keyboard. |
| UpdateDialog | Cut | — | Store-delivered; already no-op. |
| StartMenu / StartScreen | Adapt | P1 | StartScreen works; "More" tab covers backstage. |

## 5. Settings — show only what makes sense on mobile

Keep: **Appearance** (language/theme/accent), **Editor**, **Writing Assistance**, **Templates**,
**Writing Goals** (minus *Watch filesystem* — desktop file-watching).
Hide on phone: **Hotkeys** (no physical keyboard; keep for iPad + external keyboard), **self-update**
toggle (store-delivered), **Watch filesystem**, **log-folder reveal** (or replace with "share log"),
**Extensions** (deferred). **GitHub token** only if Git ships. Mac-App-Store gating already exists
via `window.novalist.isMas` — add an analogous `isMobile` gate.

## 6. Gestures / hotkeys → touch

- Right-click context menus (binder, editor, scene tabs) → **long-press**.
- Hover (entity peek, binder actions) → **tap** (binder already has +/⋯; editor peek TODO).
- Drag-reorder (binder, corkboard) → **explicit up/down / move controls** (drag fails on touch).
- Resize handles (binder/inspector/notes) → **not needed** (single pane / sheets).
- Keyboard shortcuts → keep for **iPad + external keyboard**; irrelevant on phone.

## 7. Chrome

- **Toolbar** — hidden on mobile; its actions (new chapter/scene, find, snapshots, panel toggles,
  book/draft selectors) must resurface: New Chapter/Scene = done (Binder); Find = Search tab;
  Snapshots/Inspector/Notes = P1 (sheets); **Book/Draft selectors = P1 (need a home — a header
  menu on the Write tab)**.
- **StatusBar** — hidden; useful bits (scene word count, goal progress, backend/connection) could
  surface as a slim header or in the dashboard. P2.
- **ActivityBar** — replaced by the native tab bar. Deferred views simply aren't in the bar.

---

## Cross-cutting / platform tasks (your explicit asks)

| Task | Treatment | Prio | Notes |
|---|---|---|---|
| Native tab-bar **localization** | Adapt | P0 | Web pushes localized titles to the UITabBar; re-push on language change. Small. |
| **Portrait + landscape** | Adapt | P0/P1 | Layout reflow, safe areas, tab bar + keyboard in landscape. |
| **Real directory picker** | Redesign | P0 | iOS document picker → security-scoped URL + persisted bookmark; wire `beginProjectAccess`/`endProjectAccess`. **Also the Git workflow: open a repo folder cloned by an external Git app.** |
| **Relative recents paths** | Fix | P1 | Container UUID changes on reinstall → absolute recents break (found in Phase 3). Moot once external folders (stable bookmarks) are the norm. |
| Native Git in-app | Cut | — | Replaced by external-folder open + external Git client. Optional later: read-only changed-files indicator. |
| **iPad adaptive multi-pane** | Defer | P2 | Device-idiom layout. |

---

## v1 execution order (decisions applied)

1. Tab-bar localization (small; closes the language gap).
2. Portrait/landscape.
3. Mobile-only settings filter.
4. **Real directory picker** (security-scoped URLs + bookmarks) — unblocks the external-Git /
   GitHub workflow and external storage; also fixes recents.
5. Inspector/Context + Footnotes + Scene Notes as bottom sheets (the writing hub).
6. Comment gutter + editor long-press menu + selection-toolbar touch.
7. Binder move/reorder; Book/Draft selector home.
8. Codex touch pass.
9. Deferred: timeline / plotGrid / calendar / relationships / maps / extensions / split editors /
   command palette / native Git / iPad multi-pane.
