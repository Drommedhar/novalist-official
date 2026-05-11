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
