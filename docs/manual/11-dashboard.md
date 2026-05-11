# Dashboard

The Dashboard is your project's home screen. It shows totals, goals, status breakdown, pacing, and writing-quality cues — the numbers you want to glance at before sitting down to write, and the ones you want to check before declaring a draft done.

## Opening the Dashboard

Click the bar-chart icon in the activity bar, or use **View → Dashboard** (or its hotkey). When you open a project with no scene previously active, the Dashboard is the default landing view.

## What the Dashboard shows

### Header

- **Book cover image** (if set) — uploaded via the dashboard's image picker or the book editor.
- **Book name** — large title.
- **Author** (from project settings, optional).
- **Last edited** timestamp.

### Top-line stats

A row of cards with:

- **Total words** in the book.
- **Reading time** (minutes), based on 225 words per minute.
- **Chapters** — total count.
- **Scenes** — total count.
- **Characters** — number of entities of type Character.
- **Locations** — number of entities of type Location.

### Status breakdown

A bar (or stacked donut, depending on layout) showing how many chapters are at each status:

- Outline
- First Draft
- Revised
- Edited
- Final

Each segment is clickable: clicking a status filters the Manuscript view to that status.

### Writing goals

Two progress bars and number readouts:

- **Daily goal** — words written today vs your daily target. The target is set in **Settings → Writing Goals**. "Today" resets at midnight in your local timezone.
- **Project goal** — total project words vs the project target, optionally with a deadline. If a deadline is set you also see **days remaining** and a **suggested daily target** to hit the goal on time.

Both progress bars are mirrored in the status bar (bottom-right of the window) so you can keep an eye on them even when the Dashboard is not visible.

### Chapter pacing

For each chapter, a stat block:

- Word count
- A bar showing this chapter's word count relative to the book's longest chapter (gives a visual sense of pacing).
- Readability score (Flesch reading-ease) with a color-coded badge.
- Status indicator.

Look for outliers — a chapter twice as long as the others, or one with markedly worse readability than its neighbors, deserves a closer look.

The dashboard also surfaces:

- **Longest chapter**.
- **Shortest chapter**.
- **Average words per chapter**.

### Echo phrases

A section listing phrases (2- to 5-word sequences) that recur unusually often across the book, with their frequency. Useful for spotting tics, repeated metaphors, and overused turns of phrase.

Click an echo phrase to run a Find across the book.

### Recent activity

A short timeline of recent edits — which scenes were modified in the last few days, sorted by recency. Click a scene name to jump to it.

## The status-bar Project Overview

There is a second, more compact overview tucked into the **status bar** (middle button). Click the project totals to pop it open:

- The popup lists every chapter with its name, word count (with a mini bar visualizing relative length), and readability badge.
- Expand a chapter to see its scenes, each with its own word count.
- A **Rename project** button.

This Project Overview is intentionally always-visible (one click away). The full Dashboard is for deeper analysis.

## Configuring goals

In **Settings → Writing Goals**:

- **Daily word goal** — integer; default 0 (no goal). Example: 500, 1000, 2000.
- **Project word goal** — integer; total target words for the book.
- **Project deadline** — date. When set, the dashboard shows days remaining and a suggested daily pace.

Goals are per-project. Edit them as your situation changes — a daily-goal change after vacation, a deadline shift after a contract change, etc.

## Tips

- **Pick one number to track.** Most writers benefit from one daily word goal and one project goal. Resist the temptation to set ambitious targets for everything; pick the number that actually moves you and ignore the rest.
- **Use status as truth.** A draft is not "done" until every chapter is at least at First Draft; revision isn't done until everything is at Revised. The status breakdown is a useful forcing function.
- **Watch the echo phrases.** A unique stylistic flourish becomes a tic on the third repetition. The echo-phrase list is the cheap version of a copy edit pass.

## Where to go next

- [Settings](23-settings.md) — set your daily and project goals.
- [Manuscript view](10-manuscript.md) — read filtered by status.
- [Image Gallery](19-image-gallery.md) — set the book cover.
