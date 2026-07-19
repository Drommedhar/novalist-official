# Calendar & story dates

The Calendar lays your scenes out on a Gregorian calendar, using the story dates stored on scenes and chapters. Use it to see the in-world schedule of the book — what happens on which day, and at what time.

## Where the dates come from

A scene appears on the calendar when it has a resolvable story date: its own date (or date range), or, failing that, the date of its chapter or act. Dates can carry optional start/end times for hour-level precision; a scene without a time counts as all-day. A scene whose range spans several days appears on every day it covers.

Scene and chapter dates are part of the project data and are shared with the [Timeline](12-timeline.md).

## Opening the Calendar

Open it from the **Plan** group in the binder's view rail (**Calendar**), from the command palette, or with `Ctrl+6` (macOS uses Cmd).

## View modes

Buttons at the top-left switch between three modes:

### Week

Seven day columns. Each scene that falls on a day appears as a chip: all-day scenes first, then timed scenes in time order with their start time (`09:30`) as a prefix. Hover a chip for the chapter, scene title, and synopsis; click it to open the scene in the Editor.

Use this view for schedule-driven sequences — a heist hour by hour, a wedding day, a multi-day siege — and for spotting clashes where two scenes claim the same hours.

### Month

A month grid. Each day cell shows up to three scene chips; if there are more, a `+N` marker shows the overflow. Days outside the current month are dimmed.

Use this view for the in-world pace of a chapter — and for finding empty days that need filling, or proving your protagonist is over-scheduled.

### Year

Twelve month cards, each showing how many scene-days it contains. Click a month to jump into its Month view.

Use this view for macro-scale pacing and seasonal gaps.

## Rescheduling by drag and drop

In Week and Month view, **drag a scene chip onto another day** to reschedule it — the scene's story date is set to that day. This is the quickest way to shuffle the in-world schedule without editing dates by hand.

## Navigation and the anchor date

The **Previous / Next** buttons step by one week, month, or year depending on the mode; the label between them shows the visible range. The date the calendar is centered on — the **anchor date** — is saved with the project, so the Calendar reopens where you left it.

## Tips

- **Even if dates don't matter, give chapters dates.** A coarse date per chapter puts every scene on the calendar; refine individual scenes only where the schedule matters.
- **For travel-heavy stories use the Week view.** Multi-day journeys plotted day by day quickly reveal whether the travel time is plausible.

## Where to go next

- [Timeline](12-timeline.md) — the other chronological view, including manual events.
- [Chapters & Scenes](04-chapters-and-scenes.md) — chapters and scenes carry the story dates.
- [Codex](06-codex.md) — character ages can be driven by birth dates.
