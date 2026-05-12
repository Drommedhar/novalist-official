# Timeline

The Timeline is a chronological view of your story. It collects everything that has a date attached — acts, chapters, scenes, and manually-added events — and arranges them on a timeline you can zoom in and out of.

Use it to see the pacing of in-world time, to spot timeline holes, and to plan events outside any particular scene.

## Opening the Timeline

Click the calendar icon in the activity bar, or use **View → Timeline** (or its hotkey).

## What appears on the timeline

The timeline draws an event for every:

- **Act** — if the act has a date range. Shown in purple (`#9b59b6`).
- **Chapter** — if the chapter has a date or date range. Shown in blue (`#3498db`).
- **Scene** — if the scene has a date or date range. Shown in green (`#27ae60`).
- **Manual event** — anything you add directly to the timeline. Shown in orange (`#e67e22`).

Each event has:

- **Title**.
- **Date** (or date range).
- **Category** — one of Plot, Character, Location, World, Other.
- **Description** (optional).
- **Linked chapter** (optional) — clicking the event jumps to the chapter.

Items with **date ranges** (start/end) render as bars; items with a single date render as dots.

## View modes

A toggle at the top of the view switches between:

- **Vertical** — events flow top-to-bottom in a single column. Easy to scroll through.
- **Horizontal** — events laid left-to-right along a horizontal axis. Good for visualizing pace.

The choice persists per book.

## Zoom levels

A second toggle changes the time granularity:

- **Day**
- **Month**
- **Year**

The labels along the time axis change accordingly. Zoom out to see the whole book at once; zoom in to see day-by-day scheduling for a tightly-timed thriller.

## Navigation

The toolbar has:

- **Previous / Next** — moves the visible window by one zoom unit (one day, one month, or one year).
- **Today** — re-center on today's real-world date.
- **Jump to date** — opens a date picker (uses the active in-world calendar if one is configured for the book; see [Calendar](13-calendar.md)).

## Filtering

Three filter controls:

- **Character** — only show events linked to (or referencing) a specific character. The dropdown lists characters from the active book.
- **Location** — only show events at a specific location.
- **Source** — limit by event source (Act / Chapter / Scene / Manual). Use this to hide chapter rows when you want to see only manually-added events.

Filters combine. Active filters are indicated next to the toolbar.

## Adding a manual event

Click **+New event** on the timeline toolbar. An inline form appears:

- **Title** — short label.
- **Date** — date or date range picker (uses the active in-world calendar).
- **Category** — Plot / Character / Location / World / Other.
- **Description** — optional long text.
- **Linked chapter** — pick from the active book's chapters; the event will jump to that chapter when clicked.

Press **Save** and the event appears on the timeline. Manual events are stored on the book.

## Editing and deleting events

- **Auto-derived events** (act / chapter / scene) cannot be edited from the timeline — change their date by editing the act, chapter, or scene directly.
- **Manual events** can be edited by clicking them and pressing **Edit**, or deleted via **Delete**.

## Tips

- **Set chapter dates first.** Even if your story doesn't depend on dates, a relative date is enough to make the timeline meaningful — "Day 1", "Day 3", "Year 2 Spring".
- **Use ranges where it matters.** A chapter that spans three weeks tells a different pacing story than three chapters that span one day each. Use date ranges to capture that.
- **Filter by character to spot off-screen time.** A character filter shows you where they were "on screen" and where they were not — easy way to spot a character who disappears for half the book and reappears without explanation.

## Where to go next

- [Calendar](13-calendar.md) — Gregorian and fully custom calendar systems.
- [Chapters & Scenes](04-chapters-and-scenes.md) — set dates and date ranges on chapters and scenes.
- [Plot Grid](08-plot-grid.md) — orthogonal view (plotlines × scenes).
