# Novalist project rules

Rules in this file apply to every Claude Code session in this repo. They override generic defaults and persist across conversations.

## When unsure, ASK — always

If a request is ambiguous, has more than one plausible interpretation, or you are about to make a non-trivial design/scope decision: **stop and ask the user before implementing.** Do not guess. Do not pick "the most likely" reading and run with it.

**Why:** guessing wrong on a multi-file feature wastes a full build cycle and the user's time, and it has happened repeatedly. A 10-second clarifying question is always cheaper than a wrong implementation.

**How to apply:**

*   Ask BEFORE writing code, not after. One tight, specific question (or a short numbered list of options) — not "should I proceed?".
*   This applies even under time pressure or when the user seems impatient. A wrong big change is worse than a question.
*   Small, reversible, obvious things (a typo fix, an unambiguous one-liner) don't need a question. Anything touching multiple files, the data model, or UX behaviour does if there's any doubt.
*   If the user already answered a question, don't re-ask it — read carefully.

## No emojis

Do NOT add emoji glyphs anywhere — locale JSON, C# / TypeScript / JavaScript code (labels / log prefixes / log tags), CSS, prose responses, menu items, button content, finding-type markers, or any other surface.

This covers all pictographs in the Unicode emoji blocks:

*   `U+1F300`–`U+1FAFF`
*   `U+2600`–`U+27BF`
*   common offenders: `✒ ✂ 💡 🎨 🎭 🔗 📊 📝 🗑 ➤ ⚠ ➕`

**Acceptable visual markers:**

*   SVG path-geometry strings (Lucide-style, e.g. `M21 15a2 2 0 0 1-2 2H7l-4 4V5...`) or `lucide-react` icons used on activity-bar entries, buttons, and other UI. These are the project's icon system.
*   Non-emoji unicode punctuation when needed and no SVG exists: `× ✕ → ←` for close / arrow buttons.
*   Plain text labels — always preferred.

**Why:** user has stated explicitly that emojis make the app feel like dumb consumer software. This was reinforced by removing every emoji previously introduced (inline actions, context menus, story-analysis filters, chat buttons, finding type icons). Treat this as a hard product-aesthetic constraint, not a stylistic suggestion.

**How to apply:**

*   When adding a new menu item, button, or locale string: use a text label and, if an icon is needed, a `lucide-react` icon or an SVG path. Never reach for an emoji as a quick visual marker.
*   When touching a file that already contains emojis (in UI, locales, or labels): strip them as part of the change.
*   Do not put emojis in Debug.WriteLine or console.log prefixes either (e.g. avoid `[💡 InlineActions]` — use `[InlineActions]`).

## New dedicated views need an activity bar entry

Every new "dedicated" view (top-level content the user navigates to — same class as Dashboard, Timeline, Codex, Manuscript, Calendar, Relationships graph, Plot Grid, Research, etc.) MUST also get an entry in the activity bar (`app/src/renderer/src/stores/shellStore.ts` → `activityGroups`, rendered by `ActivityBar.tsx`) so the user can actually find it. Hotkeys and command-palette entries alone are insufficient — the user has stated explicitly that views without activity-bar buttons are invisible to them.

**What counts as a "dedicated view":**

*   A new `xxxView.tsx` rendered by `MainArea.tsx` for a `MainView` value (the `mainView` state in `shellStore.ts`).
*   Anything that fills the centre content area and is switched to via `setMainView(...)`.

**What does NOT count (no activity bar required):**

*   Dialogs / overlays (e.g. snapshots dialog, find/replace dialog, story-date-range dialog).
*   Sidebar panels (Context sidebar tabs, Footnotes panel, Smart Lists panel).
*   Sub-views inside an existing content view (Corkboard inside Manuscript, etc.).
*   Popups (Focus peek, Comment gutter).

**Activity bar conventions:**

*   Add the view's key to the `MainView` union and to the appropriate group in `activityGroups` (`shellStore.ts`) — the Write / Plan / World / Publish blocks. `ActivityBar.tsx` renders the groups automatically.
*   Render the view for its `MainView` value in `MainArea.tsx`.
*   Give it a `lucide-react` icon (or an inline SVG path) in `ActivityBar.tsx` — never an emoji.
*   Add its label/tooltip locale key to `app/src/renderer/src/locales/en.json`, `de.json`, and `zh-CN.json` (locale-doctor gates these).

**How to apply:**

*   When you ship a new dedicated view, also add the activity-bar button in the same change. Do not split this into a follow-up.
*   If you're unsure whether something qualifies as a "dedicated view" (e.g. it's a hybrid panel, or it might end up nested inside another view): **ask the user before shipping.** Do not assume.

## Sizes come from design tokens — never hardcode

`app/src/renderer/src/styles/tokens.css` defines the canonical scales for font size, spacing, corner radius, and colour as CSS custom properties (`--nl-*`) on `:root`. They are available anywhere in the renderer.

**Do NOT hardcode size or colour literals in CSS.** Use the tokens via `var(--nl-...)`:

*   **Font size** — `font-size: var(--nl-font-body)`. The scale runs from `--nl-font-caption` up through `--nl-font-body`, `--nl-font-title`, `--nl-font-display` (see `tokens.css` for the full set).
*   **Corner radius** — `border-radius: var(--nl-radius-md)` (`--nl-radius-sm` / `-md` / `-lg` / `-xl` / `-pill`).
*   **Spacing** — `gap` / `padding` / `margin: var(--nl-space-md)` (`--nl-space-tightest` … `--nl-space-xxl`).
*   **Colour** — surfaces, text, borders, and the accent are tokens too (`--nl-surface-*`, `--nl-text-*`, `--nl-border`, `--nl-accent`). Never hardcode hex in component CSS.

**How to apply:**

*   Any new or edited CSS: use `var(--nl-...)` for font size, spacing, radius, and colour — both in rule bodies and in inline `style={{ }}`.
*   Pick the nearest token rather than inventing an off-scale value. If a genuinely new size or colour is needed, **add a token to** `tokens.css` and use it — do not hardcode.
*   When you touch a file that still has hardcoded sizes or colours, convert them as part of the change.

## Feature changes must update the user manual (and possibly README)

The canonical user-facing documentation lives in `docs/manual/` (entry point `docs/manual/README.md`) with one page per feature area, plus a top-level `README.md` that lists the headline features. When you change Novalist's feature surface, you MUST update both in the same change so the docs never drift from the code.

**What counts as a "feature change":**

*   Adding a new dedicated view, dialog, sidebar tab, status-bar item, toolbar button, or command-palette entry.
*   Adding or removing a hotkey, or changing a default gesture.
*   Adding or removing an entity field, custom-property type, export format, project template, or settings option.
*   Renaming a feature, a section in Settings, or a menu item the user sees.
*   Changing or removing existing user-visible behavior (e.g. dropping auto-replacement for a language, swapping out grammar-check provider, changing the snapshot folder layout).
*   Changing the on-disk project layout (`.novalist/`, `Books/`, `WorldBible/`, snapshot folder, etc.).
*   Adding or removing an SDK hook interface, or changing the public SDK surface.

**What does NOT count (no docs update required):**

*   Pure refactors, renames of internal identifiers, dependency bumps.
*   Bug fixes that restore documented behavior.
*   Build / CI / packaging changes that don't surface to the user.
*   Visual polish (spacing, colors, icon tweaks) that doesn't add or rename a control.

**How to apply:**

*   For each feature change, decide whether an **existing manual page** covers the area and edit it, or whether a **new page** is needed. Use a new page only for a genuinely new top-level feature; otherwise extend the closest existing page.
*   When adding a new page, give it the next numeric prefix (`NN-slug.md`) and add it to the table of contents in `docs/manual/README.md` in the correct section. Cross-link from any related page's "Where to go next" footer.
*   When renaming or removing a feature, search the whole `docs/manual/` tree for stale references — including link targets — and fix them.
*   Headline features mentioned in the top-level `README.md` "Features" sections must be kept truthful too. Update or add bullets when a feature is added, removed, or significantly reshaped. Granular sub-features can live only in the manual.
*   Update `docs/manual/26-hotkeys.md` whenever default hotkey bindings change. The source of truth is the hotkey list in `app/src/renderer/src/shell/hotkeys.ts`; the manual must match.
*   Update `docs/manual/27-localization.md` if the set of bundled languages changes.
*   Update `docs/extension-guide.md` if the SDK surface changes; mention SDK breaking changes in the manual's Extensions page as well.
*   Keep the same no-emoji rule that applies to the rest of the project. Use plain text labels and Markdown formatting. SVG / emoji glyphs do not belong in docs prose either.
*   If you're unsure whether a change is user-visible enough to warrant a docs edit: **err on the side of editing.** A one-line addition that turns out unnecessary costs nothing; a missed docs update means the manual is wrong on the very next read.

## Every commit MUST update CHANGELOG.md

`CHANGELOG.md` at the repo root is the user-facing release history for the desktop app. It is written for writers using Novalist, not for developers reading diffs. Every commit that changes anything a user could notice MUST add its entry to the **Unreleased** section in the same commit — never as a follow-up.

**Format:** [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) with semantic versioning. Newest release first. Inside a release, entries are grouped under these headings, in this order, omitting the empty ones:

*   **Added** — new feature, view, dialog, setting, format, hotkey, provider, language.
*   **Changed** — existing behavior that now works differently, including UI reorganizations and defaults.
*   **Fixed** — bugs. Describe the symptom the user saw, not the internal cause.
*   **Removed** — features, formats, settings, or SDK surface that are gone.
*   **Security** — anything with a security or privacy impact.

**How to apply:**

*   Work in progress lands under the topmost `## [Unreleased]` heading. **Never invent a version number for it** — the release workflow stamps the real one from the pushed tag. If the section is missing, add it back as a bare `## [Unreleased]`.
*   Releasing is automatic and you do not do it by hand. On a stable tag, `.github/workflows/release.yml` reads the Unreleased section, publishes it as the GitHub release notes, then runs `tools/changelog.py release` to rename the heading to `## [X.Y.Z] - <tag date>`, add the compare link, open a fresh Unreleased section, and push that back to the default branch as `docs(changelog): release X.Y.Z [skip ci]`. Prerelease tags (any tag containing `-`) publish the notes but deliberately leave the Unreleased section intact.
*   Use `python tools/changelog.py extract --version 2.1.1` to read a past section, and `--unreleased` for the pending one.
*   One bullet per user-visible change, in plain language. Say what the user can now do or what stopped going wrong — not which class or RPC method changed. Bold the feature name when a bullet introduces one (`**Wiki view** — ...`).
*   Do not name files, classes, RPC namespaces, or commit hashes. Do not reference internal milestone or plan names (M3, "parity wave 2"). Do not paste commit subjects verbatim.
*   iOS / mobile-only work does not belong in this file — the mobile app releases under its own `ios-*` tags. If a change touches both, write the desktop half only.
*   The same no-emoji rule applies here.

**What does NOT need an entry:** pure refactors, internal renames, dependency bumps, test-only changes, CI/build changes that do not alter what ships, and documentation-only edits. If a "refactor" changed behavior at all, it needs an entry.

**Why:** the changelog is what users read to decide whether to update and to understand what changed after they do. Reconstructing it from terse commit subjects after the fact is guesswork — the early 1.x history had to be rebuilt from diffs because the messages said "More" and "Fixes". Writing the entry while the change is fresh is the only point at which it is cheap and accurate.

## Never write the CI skip token into a commit message you did not mean to skip

GitHub Actions scans the **head commit message** of a push for `[skip ci]` (and `[ci skip]`, `[no ci]`, `[skip actions]`, `***NO_CI***`) and suppresses **every** workflow for that push. It does not care where in the message the token appears — subject or body — and it applies to tag pushes too, because a tag push is a push whose head commit is the tagged commit.

**How to apply:**

*   Only use the token when you actually want the push skipped — a version bump or a changelog stamp made by CI itself.
*   When a commit message *describes* skip behaviour, do not spell the token out. Write "with the CI skip token" or "skip-ci" instead. Prose in a tracked file (this one, a workflow YAML, the manual) is fine — only commit messages are scanned.
*   If a tag produced no workflow run at all, check the tagged commit's message for the token before looking at triggers or job conditions. Re-pushing the tag will not help; the message is what is scanned. Move the tag to a commit whose message is clean.

**Why:** the 2.2 tag silently produced no release. The commit it pointed at explained that the release job pushes its changelog stamp with the token — and spelling it out in the body was enough to suppress the tag's own release run and the branch's CI run. The failure is invisible: no run appears, no error is reported, and the tag looks fine.

## The diagnostic log must NEVER contain story content

Novalist has an opt-in diagnostic file log (Settings → Diagnostics, `AppSettings.DiagnosticLoggingEnabled`). It exists so users can send us a log to debug issues we cannot reproduce. Users must be able to send it without fear that their writing is exposed. Treat this as a hard content-policy and content-policy-compliance constraint, not a style preference.

**The pipeline:** every `Log.Debug/Info/Warn/Error` line is written to `%APPDATA%/Novalist/logs/` when the user opts in. `Log` runs each line through `LogRedactor` as a backstop (strips filesystem paths to their extension, drops over-long blobs), but the **primary** guarantee is the allowlist: callers must only pass structured, non-content data.

**Never pass to** `**Log.***`**:**

*   Scene / chapter / book / project / entity titles or names.
*   Scene text, notes, comments, footnotes, synopses, descriptions, or any prose the user wrote.
*   Character / location / item / lore field values, custom-property values, tags, POV, conflict, emotion text.
*   Full filesystem paths, project folders, or file names (the redactor strips these, but do not rely on it — omit them).
*   Anything relayed from the renderer / editor iframes that could echo user data — keep it out of `Log.*`; route raw diagnostic text to `Debug.WriteLine` (debugger only).

**Safe to log:** state names, enum / type names, counts, booleans, sizes/dimensions, timings, GUID-style identifiers, exception types and stack traces, version / OS / runtime / culture.

**How to apply:**

*   When adding a `Log.*` call, log the _shape_ of the situation, not the content. Prefer `count={list.Count}` over the items, `len={text.Length}` over the text, `id={guid}` over the title.
*   When you touch a file that logs a title / name / path / user string, redact it as part of the change (drop it, or replace with a count / length / id).
*   If a genuinely useful diagnostic seems to need user content, it does not — find a content-free proxy, or ask the user. Never weaken the redactor to let content through.

## New code must ship with tests — coverage is gated at 100%

`Novalist.Core`, `Novalist.Sdk`, and `Novalist.Backend` are at **100% line coverage**, enforced in CI (`.github/workflows/ci.yml` → `eng/Check-Coverage.ps1`). A push or PR that drops any of the three below 100% fails the build. Treat the gate as a hard constraint, not a nice-to-have.

**How to apply:**

*   Every new or changed unit of behavior — service, RPC method, utility, model method, host bridge — ships with unit tests **in the same change**. Do not split tests into a follow-up.
*   Filesystem / process / network / RPC code uses the established seams: temp-dir fixtures, `NSubstitute` fakes for interface boundaries, and the in-memory JSON-RPC pair (`FullDuplexStream`) for backend contract tests.
*   Do **not** weaken the gate to land code. Don't add `[ExcludeFromCodeCoverage]` just to dodge a hard-to-test line — refactor it behind a seam, or extract the genuinely-untestable interop into a small, clearly-named excluded method/class **with a one-line reason comment**. Native interop (real process launch, installer, real network) is the accepted exclusion category; everything else is testable.
*   The coverage check dedupes cobertura line fragments by `max(hits)` per source line (`tests/coverlet.runsettings`).

**The Electron renderer (`app/`)** is not on the 100% C# gate. It is covered by TypeScript typecheck, `locale-doctor`, `token-doctor`, `rpc-doctor`, and Playwright e2e (the `web` job in `ci.yml`). Renderer behaviour changes should come with an e2e assertion where feasible.

**100% coverage does not mean the feature works.** Line coverage answers "did a test execute this line", never "does production reach it". A unit that is correct, directly tested, and called by nothing scores exactly like a load-bearing one — which is how the Settings project-override switch shipped broken while `Novalist.Core` sat at 100%: the `Has*Override` properties behind it were tested and wired to nothing. When you add a backend capability, the same change must call it from the renderer and assert the behaviour end-to-end. `python tools/rpc-doctor.py` catches the RPC half of this: a renderer call to a method that does not exist fails the build, and a backend method nothing calls is reported.