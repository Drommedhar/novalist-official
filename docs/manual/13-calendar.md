# Calendar & in-world dates

Novalist can run on either the standard Gregorian calendar or a fully custom calendar with your own month names, day counts per month, weekday names, and year label. This means a fantasy or sci-fi novel with its own calendar system can have its in-world dates respected throughout: in scenes, on the Timeline, in date pickers, in formatted output.

## What "story dates" are

A **StoryDateRange** is a structured date attached to a chapter, scene, or timeline event. It has:

- **Start** — a date string in the active calendar's format.
- **End** — same, optional. If absent the event is a single date.
- **Start time / End time** — optional `HH:mm` strings for hour-level precision.
- **Note** — optional free text annotation shown on hover.

Story date ranges live on:

- `ChapterData.dateRange`
- `SceneData.dateRange`
- `ActData` (for acts)
- Timeline manual events

When a date range is present it takes precedence over the simpler free-text `Date` field on the same item.

## Configuring the calendar

Open the book editor or the calendar configuration screen. The **in-world calendar** has two types:

### Gregorian

The default. Dates use the standard `YYYY-MM-DD` format with the local Gregorian month and weekday names. Pickers behave like a normal date picker. Year arithmetic works as expected.

### Custom

Configure:

- **Year label** — e.g. "AC" for "After Conjunction", "AR" for "After Reckoning", or whatever your world uses.
- **Month names** — an ordered list of month names. As few or as many as your calendar has.
- **Days per month** — one integer per month. Can vary (e.g. 28, 30, 31).
- **Weekday names** — an ordered list of weekday names. Typically 5–8 depending on your world.
- **Derived year length** — sum of the days-per-month list (read-only).

Once set, the calendar:

- Drives date pickers everywhere (chapter date, scene date, timeline events, story-date-range dialog).
- Drives the **Calendar view** (described below).
- Drives the Timeline's axis labels at the lower zoom levels.

You can switch a project from Gregorian to custom or vice versa. Existing dates are stored as strings and re-interpreted on read, so a switch may leave dates looking strange — fix them as needed.

## The Calendar view

Click the multi-day calendar icon in the activity bar to open the Calendar view. It has three view modes:

### Week view

A 24-hour grid with day columns. Each scene with a `StoryDateRange` that overlaps the week is rendered as a colored block at the correct hour and day. The colour uses the scene's label color when set, otherwise a default.

Click a block to open the scene. Drag a block (in supported builds) to reschedule.

Use this view for:

- Schedule-driven scenes (a heist hour by hour, a wedding day, a multi-day siege).
- Spotting clashes where two scenes claim the same hours.

### Month view

A standard month grid. Each day cell shows the scenes that take place that day as compact pills. Multi-day events span across cells.

Use this view for:

- Sense of the in-world pace of a chapter.
- Finding empty days that need filling — or empty days that prove your protagonist is over-scheduled.

### Year view

A grid of months for the whole year. Each month cell highlights the days that contain scenes.

Use this view for:

- Macro-scale pacing.
- Spotting seasonal gaps.

## Navigation

The toolbar has:

- **Previous / Next** — moves by one unit of the active view mode (week, month, year).
- **Today** — jumps to the current real-world date in the active calendar.
- **Jump to date** — opens a date picker.

## Setting story dates on chapters and scenes

There are two ways to put a date on a chapter or scene:

1. **Simple Date field** — a free-text string. Useful if your calendar is informal ("Day 3", "Spring"). Stored as `Date`.
2. **Structured date range** — opens the **Story Date Range dialog** with calendar-aware controls. Stored as `DateRange`.

Open the dialog from the chapter or scene right-click menu (**Set date range**). The dialog asks for start, optional end, optional start/end times, and an optional note. If the active calendar is custom, the dialog's month and weekday pickers reflect your custom names.

## Where calendar dates show up

- **Calendar view** — primary display.
- **Timeline** — chapters and scenes with dates appear chronologically.
- **Chapter / scene tooltips** — date-range string in the chapter and scene hover tooltips.
- **Manuscript outliner** — date column.
- **Exports** — when an export template includes a date placeholder.

## Tips

- **Even if dates don't matter, give chapters relative dates.** "Day 1", "Day 5", "Two weeks later" is enough to drive the timeline.
- **Start with Gregorian; switch later if needed.** A custom calendar is a worldbuilding commitment. Don't reach for it on day one of a project unless you already know you need it.
- **For travel-heavy stories use the Week view.** Multi-day journeys plotted hour by hour quickly reveal whether the travel time is plausible.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — story-date fields live on chapters and scenes.
- [Timeline](12-timeline.md) — the other big chronological view.
- [Codex](06-codex.md) — character ages can be driven by the calendar via birth-date age mode.
