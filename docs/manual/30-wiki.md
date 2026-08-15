# Wiki

The Wiki is a read-only, Wikipedia-style reader over everything in your [Codex](06-codex.md). Where the Codex is for *editing* one entity at a time, the Wiki is for *reading through* your world: browsing every character, location, item, lore entry, and custom entity as a cross-linked article, following links from one page to the next, and seeing where each entity turns up across your manuscript.

Nothing here is a separate copy of your data. Every article is generated on the fly from the fields, sections, relationships, and images you already authored in the Codex, plus the scenes that mention the entity. Editing still happens in the Codex — the Wiki never changes anything.

Open **Wiki** from the **World** mode, under **In this book** in the mode panel, next to the Codex. On the phone app, it lives inside the **Codex** tab: tap the **Codex / Wiki** toggle at the top, and the Wiki opens as a full-width list — tap an entry to read its article, then use the back button to return to the list.

## The Wiki at a glance

The view has two panes:

- The **index** on the left lists every entity, grouped first by scope (**Book** and **World Bible**) and then by type (Characters, Locations, Items, Lore, and each of your custom types). Entries are sorted by name and show a thumbnail and a short subtitle. A **filter box** at the top narrows the list as you type, matching an entry's name, its subtitle, or any of its aliases; groups that end up empty disappear while you type.
- The **article** on the right shows the selected entity.

Selecting an entry in the index opens its article. Opening the Wiki selects the first entry automatically so the article pane is never empty.

## Anatomy of an article

An article is assembled from what you have filled in — empty parts are simply omitted:

- **Title** — the entity's name (a character's full name where a surname is set).
- **Type and scope chips** — the entity type and whether it lives in the current Book or the shared World Bible.
- **Lead line** — the name in bold, any alternate names ("also known as …"), and a one-line descriptor built from the key fields (for example "Aldric Vane — Knight · Grey Order", or "Harbour — City in Aldland").
- **AI summary** (optional) — a generated encyclopedic paragraph, shown only when an AI extension that provides an article generator is installed (see below).
- **Description** — for locations, items, and lore, the entity's description is shown as the opening paragraph. (Characters have no single description; their body comes from sections.)
- **Stats strip** — an at-a-glance summary for entities that appear in scenes: number of appearances, chapters spanned, POV scenes (characters only), and first / last appearance.
- **Contents** — a table of contents listing the sections below; click an entry to jump to it. Shown once there is more than one section.
- **Sections** — the free-form sections you wrote in the Codex, rendered as article body text. Entity names inside that text are linked automatically: whenever a word or phrase matches exactly one entity's name or alias it becomes a clickable link to that entity's article. You can also force a link (or link an ambiguous name) with a `[[Name]]` wiki-link, and give it different link text with `[[Name|shown text]]`. Names that match more than one entity, and text inside code spans or existing Markdown links, are left as plain text.
- **Relationships** — each role and its targets. A target that resolves to a single entity is a link; an ambiguous or unknown name is shown as plain text.
- **Referenced by** — the reverse view: other entities whose relationships or entity-reference fields point at this one.
- **Contains** — for a location, the locations whose **parent location** is this one, so a region's article lists the cities inside it. Each is a link to its own article.
- **Appears with** — entities that co-occur in the same scenes, most frequent first, with the shared-scene count.
- **Plotlines** — the plot threads the entity's scenes belong to.
- **On maps** — map pins that point at this entity; clicking one opens the map centred on the pin.
- **Research** — the [research](15-research.md) items you linked to this entity. Clicking one opens it in the Research view.
- **Events** — the manual [timeline](12-timeline.md) events that name this entity among their characters or locations, in date order with undated events last. Clicking one opens the Timeline. (Unlike Appearances, which is derived from your prose, this is the timeline you authored by hand.)
- **Changes over time** — for characters with per-act / per-chapter / per-scene overrides (see [Codex](06-codex.md)), the scopes where the character differs from the base, listing each changed field and its overridden value in manuscript order.
- **Appearances** — the scenes that mention this entity, in story order (see below).
- **Infobox** — a fact panel down the right side with the primary image (and its caption), an image gallery of any further images, and the entity's short fields (role, gender, type, parent location, category, origin, custom fields, and so on). A location's parent is a link to the parent's article.

## Appearances — the built-in timeline

The **Appearances** section lists every scene in which the entity is mentioned. A scene "mentions" an entity when its text contains an entity mention — the same links you get from the editor's entity hover cards and auto-mentions (see the [Editor](05-editor.md) page). For each appearance you see:

- the scene's resolved **story date** (from the scene's date or range, falling back to its chapter's),
- the chapter and scene name,
- and the scene's **synopsis** if one is set.

Appearances are ordered chronologically by story date, with undated scenes last, then by their order in the manuscript. This doubles as a per-character (or per-place, per-item) timeline: read straight down to follow an entity through the story. Clicking an appearance opens that scene in the editor.

**Appearances cover the book you currently have open**, not the whole project. In a multi-book project the heading says so explicitly ("Appearances in *Book Two*") — so a World Bible character shared across a trilogy shows one book's worth of history at a time. Switch books to see the rest.

Synopses come from the scene's Synopsis field, which you can write yourself in the scene-notes dock or have generated for you if you run the AI Assistant extension (see [Extensions](24-extensions.md)). The richer your synopses, the more readable the Appearances timeline.

## AI summary

If you have an AI extension installed that provides an article generator (the [AI Assistant](24-extensions.md) does), each article can carry a short, generated encyclopedic summary at the top.

- It is **on demand** — nothing is generated automatically. Click **Generate summary** to create one; the button becomes **Regenerate** once a summary exists. While it runs, the button shows a **Generating…** state.
- The summary is built from the same deterministic dossier the article is made of — the entity's fields, sections, relationships, and the scenes it appears in — so the model summarizes what you actually wrote rather than inventing facts.
- It is **cached** per entity (under `.novalist/wiki/`), so it persists across sessions and is not regenerated on every visit. When the entity's underlying data changes, the summary is flagged **Out of date** to prompt a regenerate — but it never regenerates on its own.
- With **no** article-generator extension installed, the summary and its button simply don't appear; the rest of the article is unchanged. A previously cached summary still shows (read-only) even if the extension is later removed.

## Cross-links

The Wiki is meant to be browsed by clicking:

- **Relationship targets**, **Referenced by** entries, **Contains** entries, **Appears with** chips, and the location **parent** all link to their articles.
- **Entity names inside section prose** link to the mentioned entity automatically (and you can force one with `[[Name]]`).
- **Appearances** open the scene in the editor.
- **On maps** entries open the map centred on the pin.
- The **Contents** list jumps to a section within the current article.

A name only links when it resolves to exactly one entity; names that could mean more than one entity are left as plain text so a link never sends you to the wrong page.

## Articles about the world

Every article described so far is **generated** from a Codex entry. That leaves nowhere for the writing that is about the world rather than about one thing in it — an essay on how the economy works, on what the magic costs, on why the war started. Those had to hang off whichever entry they least badly belonged to, or live in Research outside the Wiki entirely. Only Locations nested, so filing one under another was not possible either.

**Articles** sits at the top of the index, above the generated entries — they are about the world as a whole, and the generated ones are about the things inside it.

- The **+** beside the heading starts a new article. **Article inside this one**, in the article's own toolbar, starts one beneath it.
- Nesting goes **as deep as you like**. A world, a region inside it, a town inside that.
- **Sits under** re-files an article. An article cannot be filed under itself or under anything below it — that would make a ring, and everything inside the ring would still be in the file and never reachable again — so those are simply not offered rather than being offered and refused.
- The title and the body are edited **in place**: this article is written rather than derived, so it is edited where it is read.
- An article with **no title yet is still saved**. Starting an essay and naming it afterwards should not cost you the essay.
- **Deleting** an article lifts its children into its place rather than taking them with it. An article is a container as much as a piece of writing.

Selecting a written article deselects the generated one and vice versa, so exactly one is ever open.

## Editing

The Wiki is read-only by design. To change anything on a page, use the **Edit in Codex** button in the article header — it switches to the Codex with that entity selected, ready to edit. Your edits show up in the Wiki the next time you open the article.

## Spoilers

The Wiki shows everything as it currently stands — it is an author's tool, not a spoiler-free reader's companion, so late-story facts and a character's full arc are all visible.

## Where to go next

- [Codex (Characters, Locations, Items, Lore)](06-codex.md) — where all the entity data the Wiki reads is authored and edited.
- [Chapters & Scenes](04-chapters-and-scenes.md) — scene synopses and story dates feed the Appearances timeline.
- [Relationships graph](14-relationships.md) — a visual, clustered view of the same character relationships the Wiki links.
- [Extensions](24-extensions.md) — the AI Assistant can generate the scene synopses that make Appearances richer.
