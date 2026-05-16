# Calendar & in-world dates

Novalist runs on the standard Gregorian calendar today. Chapters, scenes, acts, and timeline events can carry structured dates that show up on the Calendar view, the Timeline, and in tooltips and exports.

> **Note:** A custom-calendar data model (custom month names, days per month, weekday names, year label) exists internally and can be set via a project's JSON, but there is no in-app editor for it yet. The Calendar view, date pickers, and Timeline labels currently assume Gregorian. A custom-calendar editor is planned.

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

Open the dialog from the chapter or scene right-click menu (**Set date range**). The dialog asks for start, optional end, optional start/end times, and an optional note.

## Where calendar dates show up

- **Calendar view** — primary display.
- **Timeline** — chapters and scenes with dates appear chronologically.
- **Chapter / scene tooltips** — date-range string in the chapter and scene hover tooltips.
- **Manuscript outliner** — date column.
- **Exports** — when an export template includes a date placeholder.

## Tips

- **Even if dates don't matter, give chapters relative dates.** "Day 1", "Day 5", "Two weeks later" is enough to drive the timeline.
- **For travel-heavy stories use the Week view.** Multi-day journeys plotted hour by hour quickly reveal whether the travel time is plausible.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — story-date fields live on chapters and scenes.
- [Timeline](12-timeline.md) — the other big chronological view.
- [Codex](06-codex.md) — character ages can be driven by the calendar via birth-date age mode.
