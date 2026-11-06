# Scene stages with chapter-status compatibility

The user approved removing chapter writing state from the app. Scenes alone carry writing stages. Do not add a derived chapter state or chapter-progress summary.

## Approved approach

Retain the stored chapter-status field, SDK read/write behavior, import handling, RPC contracts, and existing Smart List rules. Mark the SDK members with non-error Obsolete attributes and compatibility-only documentation, as explicitly requested by the user. Preserve runtime and binary compatibility; rebuilding callers emits warnings (including in warnings-as-errors builds). Removing the public contract outright would break unknown extensions; calculating status from scenes would change the setter's meaning. Neither is acceptable.

Remove chapter status from chapter creation/editing, binder dots, the clickable Manuscript badge, and ordinary filter choices. The Dashboard uses its existing scene-stage breakdown instead of chapter-status counts. Corkboard coloring uses scene stages and their configured colors. Preserve old filter presets explicitly: a legacy chapter restriction remains visible and individually clearable when restored, but new filters offer scene stages.

Existing Smart Lists containing chapter-status rules remain readable, editable, and evaluable. New rules offer scene fields; legacy chapter status remains available only on a rule already using it. Never rewrite chapter status into scene stages or modify the user's story project.

## Validation

Cover two first-draft scenes among outline scenes whose containing chapters have unrelated statuses, scene-color mapping, no chapter-status controls, and legacy SDK/query behavior. Use Node, backend tests, headless browser checks, typecheck, source checks, and builds. Native Electron E2E requires explicit consent for that run; earlier Notes-window consent does not authorize unrelated desktop runs.
