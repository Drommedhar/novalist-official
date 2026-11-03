# Floating scene checklist

The approved design turns scene notes into a movable checklist that remains useful while writing and researching. Existing Silent Shadows notes contain long root bullets, scene beats, nested details, and ordinary prose. Preserve their wording and hierarchy.

## Agreed behavior

- Automatically interpret bullet lists as checklist items, including pasted bullets. Persist completion in the scene's existing notes text using task markers, preserving free text and nesting.
- Root items have a checkable title and expandable details. Details may contain prose, nested checklist items, or both. Keep long flat bullets intact; never invent a shorter title. Parent and child completion are independent.
- Show progress and group completed root items in a collapsible section.
- Use a separate movable, resizable desktop panel with Focus Peek-like controls. Remember placement. Pinned mode keeps it above the active Novalist window only; other apps and their desktops must not be covered. Auto-hide dismisses it on leaving the window. Hide and the existing scene-notes shortcut toggle visibility. A visible Notes control restores it from any main view and Focus Mode.
- Use a compact draggable header with pin and hide controls, no native minimize/maximize/close buttons, and no persistent save-status footer. Keep failed-save feedback visible. Use native Liquid Glass on supported macOS versions and vibrancy on older macOS.
- Follow the active scene by default, retaining its context while visiting other views. A separate follow-scene control can hold the selected scene. Project/book/draft changes always invalidate old scene context.
- Replace the desktop bottom notes dock and inspector's duplicate notes editor. Keep synopsis, goal/outcome, timing, narrative mode, cast, and custom fields in the inspector. Mobile keeps its existing fields.

## Approach

The selected approach is a floating scene-specific checklist. A fixed dock fails the requested placement and reachability. The user selected a floating window after being offered an in-app overlay or a separate window. Keep the existing notes field as the source of truth rather than copy bullets into project tasks; this preserves existing projects and avoids synchronizing duplicate text.

## Implementation decisions

- A single notes window joins the existing workspace and pending-save/close protocols. Other desktop windows publish their active scene to it; the notes window owns desktop note editing.
- Native window movement/resizing supports other displays. Restore saved bounds within an attached display's work area. Pin and follow-scene are separate controls. Own the panel from the active workspace window instead of using global always-on-top or all-Spaces flags; make it fullscreen auxiliary without transforming the app process.
- Plain-text editing remains available for adding/reordering prose and lists. A quick-add field creates a root checklist item. Rendering never executes HTML.
- Bullet conversion is idempotent, preserves existing checked markers and line endings, and ignores fenced code and horizontal rules. Introductory free text remains visible; continuation text belongs to the preceding item.
- Pending edits save on debounce, checkbox interaction, scene changes, hiding, workspace transitions, and closing. Failed writes stay retryable and visible to the writer.

## Constraints and validation

Use existing React/TypeScript/Electron facilities and design tokens; no new dependencies or project schema. Localized labels in English, German, and Simplified Chinese. No emoji. Update the manual and README for the new feature surface. Wait for the running desktop release to stamp CHANGELOG.md before adding the next Unreleased entry.

Use Node tests for conversion, hierarchy, write ownership and window behavior; typecheck, build and repository doctors. Add Electron integration assertions but do not run Electron E2E locally: they require an isolated runner or explicit consent for that run.
