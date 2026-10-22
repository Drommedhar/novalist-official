# Novalist

Novalist is a writing workspace for books and their story worlds.

## Language

**Hosted project**:
A shared Novalist project whose connected clients communicate through a packet
relay. The relay does not persist project data; joining requires an online host.
See [ADR 0007](docs/adr/0007-relay-only-collaboration.md).

**Steam collaboration session**:
A shared project editing session joined through Steam that remains available while its hosting writer is online.

**Project owner**:
The collaborator who controls a shared project's membership and deletion.

**Local project copy**:
A collaborator's retained copy of a shared project, available to open and export after collaboration ends.

**Independent project copy**:
A separate project created from a collaborator's local copy, owned by that collaborator and independent of the original shared project.

**Recovered scene copy**:
A separate copy of edits preserved when the shared scene was deleted concurrently. It does not automatically restore the deleted scene.

**Codex entry**:
A named character, location, item, lore entry, or member of a custom type in a book or its World Bible.

**Custom entity type**:
A writer-defined category of Codex entries with its own fields and optional images, relationships, and sections.

**Reference list**:
A field containing multiple distinct references to Codex entries of one selected entity type.

**Multiline text field**:
A plain-text field that preserves line breaks for longer descriptions.

**Dismissed proofing issue**:
A spelling or grammar finding ignored for one occurrence until its scene is edited or reopened. Dismissing a finding does not add its word to the dictionary.

**Chapter story date**:
The in-world date or date range of a chapter. Scenes without their own dates or relative timing inherit it, falling back to the act's date when the chapter is undated.

**Act timeline date**:
The act's explicit story date, or otherwise the story date of its first chapter in manuscript order. An act with neither remains undated.
