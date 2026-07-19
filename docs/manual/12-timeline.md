# Timeline

The Timeline is a chronological view of your story. It collects everything that has a date attached — acts, chapters, scenes, and manually added events — and groups them along a time axis you can zoom in and out of.

Use it to see the pacing of in-world time, to spot timeline holes, and to plan beats that are not tied to any particular scene.

## Opening the Timeline

Open it from the **Plan** group in the binder's view rail (**Timeline**), from the command palette, or with `Ctrl+3` (macOS uses Cmd).

## What appears on the timeline

- **Acts** — each act name appears once, ahead of its first chapter.
- **Chapters** — every chapter that has a date.
- **Scenes** — every scene that has a date (a scene without its own date inherits its chapter's date). Shown as `Chapter: Scene`.
- **Manual events** — anything you add directly to the timeline.

Each entry shows a marker dot, its title, a **source pill** naming where it came from (Act, Chapter, Scene, or Events), its date, and (for scenes and manual events) a description or synopsis. Events that reference characters or locations also show them as **chips** under the description.

Entries are grouped under headers according to the zoom level (for example `2024`, `Mar 2024`, or `Mar 15, 2024`). Undated entries collect in a `???` group at the end — handy as an inbox of beats still waiting for a date.

**Dates** are free-form text but sort best in ISO form: `YYYY-MM-DD` (also accepted: `YYYY-MM`, `YYYY`, `D.M.YYYY`).

## The toolbar

- **Add Event** — create a manual event (see below).
- **Export Outline** — pick a file and Novalist writes the timeline as a Markdown outline.
- **Add structure...** — story-structure templates (see below).
- **Character filter** — only show events that reference a specific character. The dropdown appears once at least one event references characters.
- **Location filter** — same, for locations.
- **Source filter** — limit to **Acts**, **Chapters**, **Scenes**, or **Events** (manual). Use this to hide the chapter rows when you only want your planned beats.
- **Vertical / Horizontal** — toggles the layout direction. Vertical flows top-to-bottom for scrolling; horizontal lays groups left-to-right for a sense of pace.
- **Zoom** — cycles the grouping granularity: **Year → Month → Day**.

Filters combine. The layout direction and zoom level are saved with the project.

### Date navigation

To the right of the toolbar are controls for moving the visible window along the time axis:

- **Previous / Next** — step the scroll position back or forward by one unit of the current zoom (a year, month, or day) and highlight the group you land on.
- **Today** — jump to the group nearest today's date.
- **Jump to date** — pick any date; the timeline scrolls to the matching group, or the nearest dated group when nothing sits in that exact bucket.

## Story-structure templates

The **Add structure...** dropdown appends a bundled set of beats to the timeline as manual events:

- **Three-Act** — Setup, Confrontation, Resolution; 8 beats.
- **Save the Cat** — Blake Snyder's 15-beat structure.
- **Hero's Journey** — the 12-stage monomyth.
- **7-Point Story** — Dan Wells' 7-point structure.

The beats arrive undated (they land in the `???` group). Work through them: open each beat, give it a date, link it to the chapter that delivers it, and replace the stock description with your own. Applying a template never touches your chapters or scenes — it only adds manual events, which you can edit or delete individually.

## Manual events

Click **Add Event** (or click an existing manual event) to open the event editor:

- **Name** — short label.
- **Date** — optional, e.g. `1043-03-01`.
- **Category** — Plot Point, Character Event, Location Event, World Event, or **Other**.
- **Link to Chapter** — optional; associates the event with a chapter. When set, the event shows an **Open Chapter** button that jumps to that chapter's first scene in the Editor.
- **Description** — optional long text.

**Save** stores the event with the project. When editing, the dialog also offers **Delete**; alternatively, right-click a manual event on the timeline to delete it.

Act, chapter, and scene entries cannot be edited from the timeline — change their dates on the item itself. Clicking a scene entry opens that scene in the Editor; clicking a chapter entry opens the chapter's first scene.

## Tips

- **Set chapter dates first.** Even a coarse date per chapter is enough to make the timeline meaningful; scenes inherit it automatically.
- **Use a template as a checklist, not a mold.** Apply Save the Cat, then delete the beats your story genuinely does not have — the ones left undated at the end are your gaps.
- **Filter by character to spot off-screen time.** A character filter shows where someone was "on screen" and where they were not — an easy way to catch a character who disappears for half the book.

## Where to go next

- [Calendar](13-calendar.md) — the day/week/month calendar view of the same dates.
- [Chapters & Scenes](04-chapters-and-scenes.md) — chapters and scenes carry the dates.
- [Plot Grid](08-plot-grid.md) — the orthogonal view (plotlines × scenes).
