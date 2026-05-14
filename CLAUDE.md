# Novalist project rules

Rules in this file apply to every Claude Code session in this repo. They override generic defaults and persist across conversations.

## When unsure, ASK — always

If a request is ambiguous, has more than one plausible interpretation, or you are about to make a non-trivial design/scope decision: **stop and ask the user before implementing.** Do not guess. Do not pick "the most likely" reading and run with it.

**Why:** guessing wrong on a multi-file feature wastes a full build cycle and the user's time, and it has happened repeatedly. A 10-second clarifying question is always cheaper than a wrong implementation.

**How to apply:**
- Ask BEFORE writing code, not after. One tight, specific question (or a short numbered list of options) — not "should I proceed?".
- This applies even under time pressure or when the user seems impatient. A wrong big change is worse than a question.
- Small, reversible, obvious things (a typo fix, an unambiguous one-liner) don't need a question. Anything touching multiple files, the data model, or UX behaviour does if there's any doubt.
- If the user already answered a question, don't re-ask it — read carefully.

## No emojis

Do NOT add emoji glyphs anywhere — XAML, locale JSON, C# code (labels / Debug.WriteLine prefixes / log tags), JavaScript, prose responses, menu items, ribbon entries, button content, finding-type markers, or any other surface.

This covers all pictographs in the Unicode emoji blocks:
- `U+1F300`–`U+1FAFF`
- `U+2600`–`U+27BF`
- common offenders: `✒ ✂ 💡 🎨 🎭 🔗 📊 📝 🗑 ➤ ⚠ ➕`

**Acceptable visual markers:**
- SVG path-geometry strings (Lucide-style, e.g. `M21 15a2 2 0 0 1-2 2H7l-4 4V5...`) used in `IconPath` on ribbon items, activity-bar entries, sidebar contributors, ContentViewDescriptor etc. These are the project's icon system.
- Non-emoji unicode punctuation when needed and no SVG exists: `× ✕ → ←` for close / arrow buttons.
- Plain text labels — always preferred.

**Why:** user has stated explicitly that emojis make the app feel like dumb consumer software. This was reinforced by removing every emoji previously introduced (inline actions, context menus, story-analysis filters, chat buttons, finding type icons). Treat this as a hard product-aesthetic constraint, not a stylistic suggestion.

**How to apply:**
- When adding a new menu item, button, ribbon entry, descriptor, or locale string: use a text label and either an empty `Icon` field or an SVG `IconPath`. Never reach for an emoji as a quick visual marker.
- When touching a file that already contains emojis (in UI, locales, or labels): strip them as part of the change.
- Do not put emojis in Debug.WriteLine or console.log prefixes either (e.g. avoid `[💡 InlineActions]` — use `[InlineActions]`).

## New dedicated views need an activity bar entry

Every new "dedicated" view (top-level content tab the user navigates to — same class as Dashboard, Timeline, Codex Hub, Manuscript, Calendar, Relationships graph, Plot Grid, Research, etc.) MUST also get an entry in the activity bar in `Novalist.Desktop/MainWindow.axaml` so the user can actually find it. Hotkeys and command-palette entries alone are insufficient — the user has stated explicitly that views without activity-bar buttons are invisible to them.

**What counts as a "dedicated view":**
- New `xxxView.axaml` registered as `ActiveContentView` (e.g. `IsXOpen`, `OpenXCommand`, switched in `MainWindow.axaml.cs` `UpdateContentVisibility`).
- New content-tab descriptors added to `QueueSyncContentTabs` output.
- Anything reached via `ShowXCommand` / `OpenXCommand` that fills the main content area.

**What does NOT count (no activity bar required):**
- Dialogs / overlays (e.g. snapshots dialog, find/replace dialog, story-date-range dialog).
- Sidebar panels (Context sidebar tabs, Footnotes panel, Smart Lists panel).
- Sub-views inside an existing content view (Corkboard inside Manuscript, etc.).
- Popups (Focus peek, Comment gutter).

**Activity bar conventions:**
- Location: `Novalist.Desktop/MainWindow.axaml`, the top `StackPanel Grid.Row="0"` inside `Border Classes="activityBar"`. Put the new button alongside the existing `Dashboard / Timeline / CodexHub / Manuscript / Calendar / Relationships` block; below the separator is the activity-view block (Export / Gallery / Git).
- Use an SVG `Path Data="{StaticResource IconX}"` icon. If no existing `Icon*` resource fits, add a new `<StreamGeometry x:Key="IconX">` to `App.axaml` (Lucide-style path). Never an emoji.
- Wire `Classes.active` to `ActiveContentView` with the matching `ConverterParameter`.
- Bind `Command` to the existing `OpenXCommand` (or `ShowXCommand`).
- Add a `ToolTip.Tip` bound to `{loc:Loc ribbon.xTooltip}` plus matching `en.json` and `de.json` entries.
- Respect `IsVisible="{Binding IsProjectLoaded}"` (or `IsInGitRepo` for git-only views) so the button only appears when relevant.

**How to apply:**
- When you ship a new dedicated view, also add the activity-bar button in the same change. Do not split this into a follow-up.
- If you're unsure whether something qualifies as a "dedicated view" (e.g. it's a hybrid panel, or it might end up nested inside another view): **ask the user before shipping.** Do not assume.

## Anything that opens over a WebView must snapshot-hide it FIRST

Novalist hosts native WebView2 controls (the scene editor, manuscript view, and the interactive map). On Windows the WebView is a native HWND that renders **on top of every Avalonia overlay** — the "airspace" problem. Any dialog, flyout, menu popup, context menu, color picker, or other floating UI that can appear while a WebView is on screen will be drawn *behind* the WebView and be invisible/unclickable unless the WebView is hidden first.

**The pattern:** before showing the overlay, capture a bitmap snapshot of the WebView and swap it for a static `Image`, then set the WebView `IsVisible = false`. Restore on close. This is `EditorView.SetWebViewVisible` / `MapView.SetWebViewVisible`, and centrally `MainWindow.UpdateWebViewVisibility` (driven by `_isDialogOpen` + the start-menu/settings/overview/extensions flags). `MainWindow.ShowDialogOverlayAsync` already does this for every dialog routed through it.

**How to apply:**
- Routing a new dialog through `ShowDialogOverlayAsync`? It is already covered — nothing extra needed.
- Adding a `Flyout` / `MenuFlyout` / `ContextMenu` / inline popup to a view that hosts a WebView (or could be shown while one is visible)? Hide the WebView on the flyout's `Opened` and restore on `Closed` **before** the popup renders. For the map view use the `WireFlyoutAirspace` helper in `MapView.axaml.cs`; mirror that pattern elsewhere.
- The hide must happen **before** the overlay is shown, not after — otherwise it flashes behind the WebView for a frame.
- On `Closed`, do **not** unconditionally re-show the WebView: if a flyout command opened a dialog overlay, check `MainWindow.IsDialogOverlayOpen` (or let `UpdateWebViewVisibility` own it) so you don't pop the WebView back on top of the dialog.
- JS-side / in-WebView popups (the map's own context menus drawn inside `map.html`) are fine — they're inside the HWND, not an Avalonia overlay. This rule is only about Avalonia UI that would be occluded by the WebView.
- If you're unsure whether a surface can ever co-exist with a visible WebView: **treat it as if it can** and wire the snapshot-hide. A needless hide is invisible to the user; a missed one is a bug report.

## Sizes come from DesignTokens — never hardcode

`Novalist.Desktop/Assets/Themes/DesignTokens.axaml` defines the canonical scales for font size, spacing, and corner radius. It is merged into `App.axaml` resources, so every token is available as a `{StaticResource ...}` anywhere in the app.

**Do NOT hardcode size literals in XAML.** Use the tokens:

- **Font size** — `FontSize="{StaticResource FontSizeBody}"`. Scale: `FontSizeTiny` (9), `FontSizeCaption` (10), `FontSizeBodySmall` (11), `FontSizeBody` (12), `FontSizeBodyLarge` (13), `FontSizeTitleSmall` (14), `FontSizeTitle` (16), `FontSizeDisplaySmall` (18), `FontSizeDisplay` (26), `FontSizeDisplayLarge` (32). Also `FontWeightTitle`.
- **Corner radius** — `CornerRadius="{StaticResource RadiusMedium}"`. Scale: `RadiusNone`, `RadiusSmall` (4), `RadiusMedium` (6), `RadiusLarge` (8), `RadiusXLarge` (10), `RadiusPill`.
- **Spacing** — `Spacing="{StaticResource SpacingMedium}"` (on `StackPanel` etc., single-value only). Scale: `SpacingNone` (0), `SpacingTightest` (2), `SpacingTighter` (4), `SpacingSmall` (6), `SpacingMedium` (8), `SpacingNormal` (10), `SpacingLarge` (12), `SpacingXL` (16), `SpacingXXL` (20).

**How to apply:**
- Any new or edited XAML: bind `FontSize`, `CornerRadius`, and single-value `Spacing` to tokens. This holds for both attribute form (`FontSize="..."`) and `Style` setters (`<Setter Property="FontSize" Value="..." />`).
- Pick the nearest token rather than inventing an off-scale value. If a genuinely new size is needed, **add a token to `DesignTokens.axaml`** and use it — do not hardcode.
- `Margin` / `Padding` with multi-value strings (`"16,8"`) can't bind to a single `x:Double` token; leave those literal for now (or compose from tokens if a uniform value). The hard rule is `FontSize`, `CornerRadius`, single-value `Spacing`.
- When you touch a file that still has hardcoded sizes, convert them as part of the change.

## Feature changes must update the user manual (and possibly README)

The canonical user-facing documentation lives in `docs/manual/` (entry point `docs/manual/README.md`) with one page per feature area, plus a top-level `README.md` that lists the headline features. When you change Novalist's feature surface, you MUST update both in the same change so the docs never drift from the code.

**What counts as a "feature change":**
- Adding a new dedicated view, dialog, sidebar tab, status-bar item, ribbon button, or command-palette entry.
- Adding or removing a hotkey, or changing a default gesture.
- Adding or removing an entity field, custom-property type, export format, project template, or settings option.
- Renaming a feature, a section in Settings, or a menu item the user sees.
- Changing or removing existing user-visible behavior (e.g. dropping auto-replacement for a language, swapping out grammar-check provider, changing the snapshot folder layout).
- Changing the on-disk project layout (`.novalist/`, `Books/`, `WorldBible/`, snapshot folder, etc.).
- Adding or removing an SDK hook interface, or changing the public SDK surface.

**What does NOT count (no docs update required):**
- Pure refactors, renames of internal identifiers, dependency bumps.
- Bug fixes that restore documented behavior.
- Build / CI / packaging changes that don't surface to the user.
- Visual polish (spacing, colors, icon tweaks) that doesn't add or rename a control.

**How to apply:**
- For each feature change, decide whether an **existing manual page** covers the area and edit it, or whether a **new page** is needed. Use a new page only for a genuinely new top-level feature; otherwise extend the closest existing page.
- When adding a new page, give it the next numeric prefix (`NN-slug.md`) and add it to the table of contents in `docs/manual/README.md` in the correct section. Cross-link from any related page's "Where to go next" footer.
- When renaming or removing a feature, search the whole `docs/manual/` tree for stale references — including link targets — and fix them.
- Headline features mentioned in the top-level `README.md` "Features" sections must be kept truthful too. Update or add bullets when a feature is added, removed, or significantly reshaped. Granular sub-features can live only in the manual.
- Update `docs/manual/26-hotkeys.md` whenever default hotkey bindings change. The source of truth is the `HotkeyDescriptor` list in `MainWindowViewModel`; the manual must match.
- Update `docs/manual/27-localization.md` if the set of bundled languages changes.
- Update `docs/extension-guide.md` if the SDK surface changes; mention SDK breaking changes in the manual's Extensions page as well.
- Keep the same no-emoji rule that applies to the rest of the project. Use plain text labels and Markdown formatting. SVG / emoji glyphs do not belong in docs prose either.
- If you're unsure whether a change is user-visible enough to warrant a docs edit: **err on the side of editing.** A one-line addition that turns out unnecessary costs nothing; a missed docs update means the manual is wrong on the very next read.
