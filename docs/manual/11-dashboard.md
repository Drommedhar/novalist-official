# Dashboard

The Dashboard is your project's numbers page. It shows totals, goals, streaks, status breakdown, pacing, and writing-quality cues — the numbers you want to glance at before sitting down to write, and the ones you want to check before declaring a draft done.

![The project Dashboard](images/dashboard.png)

## Opening the Dashboard

Open it from the **Write** group in the activity bar (**Dashboard**), from the **Go** menu or command palette, or with `Ctrl+2` (macOS uses Cmd).

## What the Dashboard shows

### Banner and book cover

Novalist keeps two distinct images for a project:

- The **banner** — a wide image shown across the top of the Dashboard. Use **Add banner** / **Change banner** / **Remove banner** in the banner's action row. When no banner is set, the Dashboard falls back to the book cover so projects made before this split keep showing their existing image.
- The **book cover** — a portrait image shown for the project on the welcome/start screen and in the recent-projects list. Set it in the **Book cover** panel just below the banner with **Add cover** / **Change cover** / **Remove cover**.

Both images are stored with the project. Setting only a cover on a fresh project also gives you a Dashboard banner automatically (via the fallback); set a banner explicitly when you want a different wide image from the portrait cover.

### Header and top-line stats

The project name heads the page, with the **author** underneath (when set) and a **Project Dashboard** subtitle, followed by a row of metric cards:

- **Words** — total words in the book.
- **Chapters** — total count.
- **Scenes** — total count.
- **Reading time** — estimated minutes.
- **Characters** — number of Character entities.
- **Locations** — number of Location entities.

### Daily Progress

**Click the card title to edit your daily word goal.** The card shows:

- Words written today against the daily target, with a progress bar and percentage.
- Your current **streak** — consecutive days on which you wrote.
- A **history chart** of words per day. Buttons switch the range between **30d**, **90d**, and **365d**; bars on days where you met the goal are highlighted, and hovering a bar shows the date and word count.

### Goal Tracking

**Click the card title to edit your project word goal.** The card shows:

- Total words against the project target, with a progress bar and percentage.
- When a **deadline** is set (see below), a detail row with the **deadline** date, the **days left**, and the **words per day** you need to average to hit the target on time.

### Averages

A small card pairs **average words per chapter** with the project's **estimated reading time**.

### Progress Breakdown

One row per chapter status — Outline, Draft, Revised, Edited, Final — with a status dot, a bar showing that status's share of chapters, and the chapter count plus word count at that status. Below the rows, a summary strip shows the plain chapter **count at each status**.

## Scene stages

The same shape, one level down: a row per [scene stage](04-chapters-and-scenes.md#scene-stages) in your own colours, with the scene count and word count at each. Scenes you have not given a stage are listed as **No stage set** rather than folded into the first one, so the untriaged part of the book is visible instead of quietly counted as outlined.

This is the breakdown that reflects a revision in progress. The chapter statuses above move a chapter at a time; the stages move a scene at a time, which is how revision actually happens.

### Pacing Analysis

Opens with a summary of the **longest chapter**, the **shortest chapter**, and the **average scene** length. Below it, one row per chapter shows a bar with the chapter's word count relative to the book's longest chapter. Look for outliers — a chapter twice as long as its neighbors deserves a closer look.

### Echo Finder

Phrases that recur unusually often across the manuscript, with their frequency. Useful for spotting tics, repeated metaphors, and overused turns of phrase. Consider varying your language where counts are high.

### Recent Activity

A short list of the scenes you edited most recently, each with its chapter and a timestamp — a quick way to pick up where you left off. Click any row to open that scene in the editor.

## Configuring goals

Two places:

- **On the Dashboard** — click the **Daily Progress** or **Goal Tracking** card title and enter a number.
- **Settings → Writing Goals** — **Daily Word Goal**, **Project Word Goal**, and **Project Deadline** (`YYYY-MM-DD`). The deadline can only be set here.

Goals are per-project.

## Tips

- **Pick one number to track.** Most writers benefit from one daily goal and one project goal. Pick the number that actually moves you and ignore the rest.
- **Use status as truth.** A draft is not "done" until every chapter is at least at First Draft; revision is not done until everything is at Revised. The Progress Breakdown is a useful forcing function.
- **Watch the echo phrases.** A unique stylistic flourish becomes a tic on the third repetition. The Echo Finder is the cheap version of a copy-edit pass.

## Where to go next

- [Settings](23-settings.md) — set goals and the project deadline.
- [Manuscript view](10-manuscript.md) — read the book filtered by status.
- [Chapters & Scenes](04-chapters-and-scenes.md) — where chapter statuses are set.
