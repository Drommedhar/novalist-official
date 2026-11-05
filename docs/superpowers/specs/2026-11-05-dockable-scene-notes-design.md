# Dockable scene notes

Use the existing nested pane tree, resizing and saved layouts. Notes remain an optional floating window and can dock on any edge of any content pane, including above the editor or beside the inspector. A placement overlay supplies explicit destinations; a docked panel can be moved again or floated. Notes are an auxiliary pane, not a new navigation destination. Preserve the active editor when interacting with notes.

Align the scene title and edit control in one centered row, with progress below. Use the same checklist in both presentations. Wide, shallow panels flow root items left to right; nested details remain inside their item. Keep quick-add below, and show the text editor below the checklist while editing a horizontal panel. Narrow panels retain the vertical checklist/editor toggle.

Reuse existing document sessions and save handling. Hand off ownership only after the floating renderer acknowledges successful save and unmount; failed saves keep the original surface available. Preserve follow/hold context across docking. The current notes placement, proportions, docked mode and visibility survive app restarts automatically; named saved pane layouts also include notes placement and proportions. No dependencies or project schema changes. Use design tokens and all three locales. Update manual, README and the existing Unreleased Added entry; v3.6.2 predates this feature.

Approaches: a fixed sidebar was explicitly rejected. Reusing nested panes satisfies free placement and saved layouts without a second layout engine. The implementation details above are builder choices based on the requested behavior.

Validation: pane-tree and native handoff Node tests, renderer checks, typecheck, build, repository doctors, and relevant Electron E2E. The user explicitly authorized the E2E run in this conversation.
