# Retain collaborator copies and concurrent edits

Collaborators keep local copies of the entire shared project that they can open and export after a session ends. Invited editors can change all project content, while the owner controls membership and deletion of the shared project. Access controls govern future collaboration; they cannot retract copies already received.

When a scene is deleted concurrently with another writer's edits, keep the shared scene deleted and preserve those edits in a recovered scene copy. This respects deletion without silently losing another writer's work or automatically restoring deleted content. Recovery remains separate from the shared scene until a writer explicitly restores or reuses the material.

Any collaborator may create an independent project from their retained copy and become its owner. That project has its own identity and collaboration context, so edits do not modify the original shared project.

These requirements apply to hosted projects and Steam collaboration sessions. Reconnecting to the same session may synchronize supported pending edits after validation. Fully closing one session and starting another does not trigger automatic handover or reconciliation, as recorded in [ADR 0004](0004-shared-hosted-collaboration.md).
