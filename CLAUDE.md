# Novalist project rules

Rules in this file apply to every Claude Code session in this repo. They override generic defaults and persist across conversations.

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
