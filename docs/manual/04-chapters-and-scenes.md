# Chapters & Scenes

Chapters and scenes are how Novalist breaks a book into writable pieces. A book (more precisely, its active draft) has an ordered list of chapters; each chapter has an ordered list of scenes; each scene holds the actual prose.

You spend most of your time looking at one scene in the editor and the full tree in the **binder** on the left.

## The binder tree (Chapters tab)

The **Chapters** tab of the binder shows the active draft's structure as a tree:

- **Act headers** — when chapters are assigned to acts, a header row with the act name appears above the first chapter of each act.
- **Chapter rows** — a collapse/expand chevron, a colored **status dot**, and the title. Click the dot to cycle the chapter's status.
- **Scene rows** — the scene title and its word count. Click a scene to open it in the editor; the open scene is highlighted.
- **ARCHIVE** — a section at the bottom listing archived scenes (see below).

The tree supports:

- **Drag to reorder** — drag a chapter onto another chapter to change chapter order. Drag a scene within its chapter to reorder it, or drop it onto another chapter (or one of that chapter's scenes) to move it there.
- **Right-click context menus** — on chapters: **Rename Act**, **Rename Chapter**, **Delete**. On scenes: **Archive**, **Toggle split editor**, **Rename**, **Delete**.

## Chapters

A chapter has:

- **Title** — shown everywhere.
- **Order** — its position in the book; controlled by drag-and-drop.
- **Status** — one of `Outline`, `First Draft`, `Revised`, `Edited`, `Final`. Shown as the status dot and driving the Dashboard status breakdown and the Manuscript view filter.
- **Act** — optional textual label (e.g. "Act 1"). Groups chapters under act headers in the binder and in the Timeline and Plot Grid.
- **Date / date range** — in-world dates used by the [Timeline](12-timeline.md) and [Calendar](13-calendar.md).
- **Folder name** — derived from the title at creation. Determines the on-disk folder for the chapter's scene files.

### Creating a chapter

Click **+ Chapter** in the toolbar. A dialog asks for the chapter name; confirm with `Enter`.

### Renaming a chapter

Right-click the chapter in the binder → **Rename Chapter**. The folder on disk is renamed in step with the title.

### Setting chapter status

Click the chapter's **status dot**. Each click cycles to the next status: Outline, First Draft, Revised, Edited, Final, and back to Outline. The dot's color reflects the current status.

### Reordering chapters

Drag the chapter onto the chapter you want it to take the place of. The order is persisted immediately.

### Deleting a chapter

Right-click → **Delete**. You are asked to confirm.

The chapter is **not erased**. It moves to the trash at the bottom of the binder, and its scenes go to the archive alongside it. Nothing you wrote is lost by deleting a chapter, and nothing has to be recovered from a backup.

To get it back, open **ARCHIVE** at the bottom of the binder tree and press **Restore** next to the chapter. It comes back with every scene it had, in the order it had them, at the end of the manuscript — not at its old position, because the chapters around it have moved on and renumbering them again would disturb work you have done since.

**Delete forever** next to a trashed chapter erases it and its scenes for good. That is the only action in the binder that destroys anything, and it asks first.

Snapshots of the chapter's scenes survive in the book's `Snapshots/` folder either way, until you delete them manually.

## Scenes

A scene has:

- **Title** — shown in the binder and the status bar.
- **Order** and **chapter** — its place in the manuscript; controlled by drag-and-drop.
- **File name** — the `.novalist` file on disk, derived from the title at creation.
- **Word count** — auto-computed, shown next to the scene in the binder.
- **Synopsis** and **notes** — editable in the [scene-notes dock](02-interface-overview.md#the-scene-notes-dock) at the bottom of the editor. The synopsis is also editable from the Manuscript view's outliner and appears on its corkboard cards.
- **Date / date range** — in-world dates used by the Calendar and Timeline.
- **Comments** and **footnotes** — anchored to the text. See [Editor](05-editor.md).
- **Goal** and **outcome** — what the viewpoint character is trying to get here, and what they are left with. Both are editable in the scene-notes dock and as columns in the Manuscript view's outliner. Neither is ever guessed from the prose: conflict can be read out of a scene, but a goal nobody stated and an outcome nobody wrote down are precisely what a draft is missing. Read the two columns down the outliner and a scene where nothing happens says so — the outcome repeats the goal, or there is no outcome at all.
- **Analysis overrides** — optional manual overrides for detected POV, emotion, intensity, conflict, and tags, used by Smart Lists and the Manuscript outliner.

### Relative story time

A scene can say **how long after the previous scene** it happens instead of naming a date: an amount and a unit (minutes, hours, days, weeks) in the scene-notes dock.

Novalist stored absolute dates and nothing else, so a writer who knows a scene is two hours after the last one — and neither knows nor cares which day that is — had to invent a date or leave it blank. Blank meant the scene fell out of the Calendar and the Timeline entirely, which is how a whole book ends up looking undated.

- A **date you typed** always wins and re-anchors the clock. Relative time is for the gaps, never an override.
- A scene's own offset **beats a date inherited from its chapter**: the chapter date says when the chapter starts, and "one day later" is the more specific statement.
- Offsets **accumulate**. Three scenes each an hour after the last are three hours apart, not all an hour after the anchor.
- **Negative is allowed.** A scene can be an hour *before* the one printed ahead of it — that is what a cut-back is.
- Scenes **before the first real date stay unanchored**. A book whose first date arrives in chapter nine has eight chapters with no answer, and hanging them off an invented starting point would put every one of them on the wrong day.
- **Zero** means no relative statement at all.

### Taking a scene out of the book

Right-click a scene in the binder → **Take out of the book**. The scene stays exactly where it is in the binder, the corkboard, the outliner and the Plot Grid, with its title dimmed, and leaves the manuscript: its words stop counting towards any total or target, and it is skipped by every export.

This is the step between keeping a scene and archiving it. Archiving removes a scene from every planning view, so a scene you are still deciding about had to be either fully in the book or invisible. **Put back in the book** reverses it.

Three chips above the binder tree choose what it shows: **In the book** (the default), **Everything**, or **Out of the book** on its own. Smart Lists can ask about it too, under **Out of the book**.

### Inserting a chapter in the middle

Right-click a chapter in the binder → **Insert a chapter before this one** or **Insert a chapter after this one**. Everything from that point on moves down, so chapter twelve becomes chapter thirteen and so on.

Without this the only way to put a chapter mid-book was to create it at the end and drag it up past everything after it — a dozen drags on a long book, each one a save.

Chapter **folder names on disk keep their original numbers**. Renaming them on every insert would break every snapshot path and every open editor for a purely cosmetic gain; the order you see is the `order` in the manifest, not the folder name.

### What a chapter is for

Right-click a chapter → **What this chapter is for...** stores a note in your own words. It is never printed, which is what separates it from the **subtitle** — the subtitle is what a reader sees under the chapter title in the finished book, and the description is what you left yourself.

### Creating a scene

Click **+ Scene** in the toolbar and enter a name. The scene is added to the chapter of the currently open scene (or the last chapter when nothing is open).

### Renaming a scene

Right-click the scene in the binder → **Rename**. The file on disk is renamed to match.

### Reordering and moving scenes

Drag within the same chapter to change order. Drop the scene onto another chapter to move it there; the file moves to the target chapter's folder and the snapshot history follows.

### Opening a scene in the split editor

Right-click → **Toggle split editor** opens the scene in a second editor pane beside the one you are writing in — for example to reference an earlier scene. See [Editor](05-editor.md#split-editor).

### Splitting a scene in two

Put the caret where the scene should divide, right-click, and choose **Split scene here**. Everything from the caret onwards becomes a new scene directly below the original, named "Arrival (2)" by default — split that again and you get "(3)", not "(2) (2)".

The first half keeps the original scene, so its id, its [snapshots](17-snapshots.md) and its history stay where they are. The new half inherits the metadata that still describes it: the same in-world date, the same [stage](#scene-stages), the same plotlines, the same label colour, and the same POV and analysis overrides. It does **not** inherit the synopsis — that described the whole scene, and copying it would leave two scenes claiming to be about the same thing.

Splitting at the very start or the very end of a scene does nothing, since one half would be empty.

### Merging two scenes

Right-click a scene and choose **Merge with next**. The following scene's text is appended, and it is deleted.

The surviving scene wins every conflict, because it is the one that remains and you chose the direction. Two exceptions, both because losing something is worse than keeping it: a synopsis or notes that only the second scene had are kept, and plotlines are **unioned** — a merged scene genuinely serves both threads.

Doing this by hand meant creating a scene, cutting, pasting and then repairing order, date, plotlines, stage and overrides one at a time, which is where the mistakes came from.

### Deleting a scene

Right-click → **Delete**. Asks to confirm. Snapshots of the deleted scene survive in the book's `Snapshots/` folder. If you might want the scene back, prefer **Archive**.

## Selecting several scenes at once

Scenes can be worked on as a group rather than one at a time.

- **Ctrl-click** (Cmd-click on macOS) a scene to add it to the selection, or click it again to take it back out.
- **Shift-click** selects everything from the last scene you clicked through to the one you just clicked, replacing the selection.
- A plain click opens the scene as it always did, and drops the selection.

Selecting works the same way in the binder, on the Manuscript view's corkboard cards and outliner rows, and on Calendar chips — and it is one selection shared between them, so you can pick scenes in the binder and act on them from the corkboard.

Once two or more scenes are selected, a bar appears at the bottom of the view with everything you can do to all of them at once:

- **Move to chapter** — pick a chapter and the whole selection is appended to it.
- **Add tags** — type a comma-separated list; the tags are added to every selected scene, keeping the tags they already had.
- **Shift dates** — moves every selected scene's in-world date by a number of days. See below.
- **Archive** and **Delete** — as for a single scene, and both ask first.

Right-clicking one of the selected scenes acts on the whole selection too: the menu entries say how many scenes they will affect, so **Archive (3 scenes)** never quietly does more than it says. Right-clicking a scene outside the selection acts on that one scene, as it always did.

Dragging one of the selected scenes in the binder carries the whole selection with it.

### Shifting dates in bulk

**Shift dates** opens a preview: every selected scene, the date it reads now, and the date it would read after the shift. Nothing is written until you press Apply, and scenes with no date are listed unchanged rather than hidden, so a selection of ten never previews as three. The arithmetic uses your book's own [in-world calendar](13-calendar.md#in-world-calendars), so a shift across a month boundary lands where your calendar says it should — not where the Gregorian one would.

Dragging a selected scene on the Calendar shifts the entire selection by the same number of days, keeping the gaps between the scenes intact.

## Archiving scenes

Archiving removes a scene from the manuscript without deleting its text — useful for cut scenes you are not ready to throw away.

- Right-click a scene → **Archive**. The scene leaves its chapter; it no longer counts toward totals or exports.
- Click **ARCHIVE** at the bottom of the binder tree to show what is there. Deleted chapters are listed first, then archived scenes.
- Each archived scene is listed with the chapter it left, so you can see where it belongs before deciding.
- Press **Restore** next to an archived scene to bring it back **exactly where it was** — the same chapter, in the same slot between the scenes on either side. This is the default and needs no other choice.
- To put it somewhere else instead, pick a chapter in **Restore into** first. A scene arriving in a chapter it never lived in lands at the end of it, because it has no position of its own there to claim.
- If the chapter a scene came from has since been deleted, restoring puts it in the first chapter rather than refusing — the scene exists and you asked for it back.
- Restoring a whole deleted chapter brings its scenes back in the order they had.

## How a scene sits in time

Most scenes simply happen next. Some do not, and a scene that carries a date will otherwise sort as though it happened at that point in the story.

The scene notes dock has a **How it sits in time** picker: **Flashback**, **Flash-forward**, **Parallel**, **Frame**, **Dream** or **Time skip**. A **Parallel** scene also takes a **strand** name — which thread it runs on, so two strands happening at once can be told apart instead of being one undifferentiated pile. The strand only applies to parallel scenes; changing the mode away from Parallel drops it.

The mode appears as a pill on the scene's [Timeline](12-timeline.md) entry, in both the dated and the reading-order views.

## Who and what is in a scene

Novalist works out who a scene involves from the @-mentions in its prose. Those are never wrong — you confirmed each one — but they are incomplete: a character who is in the room and says nothing leaves no mention behind, and the person a scene is really *about* is often not the one whose name appears most.

The scene notes dock has an **In this scene** box. Type a name and pick a character, location, item or lore entry to add it. Each one becomes a chip; the **star** on a chip marks the entry the scene is about.

What you assign counts as an appearance everywhere presence matters — the [Wiki's](30-wiki.md) appearance timeline, co-appearance stats, and the context sidebar — alongside the mentions found in the prose, with no double counting. A scene with an assigned cast and no mentions at all is no longer invisible to those views.

Removing an entry from the cast clears the star if it was the one marked.

## Word targets

Novalist has always had two targets — a daily one and a whole-project one, both on the [Dashboard](11-dashboard.md). A target can now also sit on a single scene, a chapter, or an act.

Set one from any of three places:

- The **Word targets** card on the [Dashboard](11-dashboard.md#word-targets), or the same panel under **Settings → Writing Goals**. Both list every target you have already set.
- Right-click the scene or chapter in the binder and choose **Set word target**. With several scenes selected, this sets the same target on all of them.
- The **Target** column of the Manuscript view's [outliner](10-manuscript.md), the fastest way across a run of scenes, since you can tab down the column.

Leave it empty, or enter 0, for no target.

A scene with a target shows a small bar beside its word count in the binder, reading `written/target`. Past the target the bar stays full and turns green rather than overflowing.

### Targets roll up

A chapter with no target of its own uses **the sum of its scenes' targets**; an act with none uses the sum of its chapters'. So putting targets on a handful of scenes already tells you where the chapter stands — you do not have to restate the same number at three levels.

Setting a target on the chapter itself overrides that sum, which is what you want when the chapter has a length in mind and the scenes inside it do not yet. Clearing it falls back to the scenes again.

## Scene stages

A chapter has a **status** — one of five fixed values, described below. A scene has a **stage**, which is whatever you say it is.

The two exist for different reasons. Revision happens scene by scene: a chapter halfway through a pass holds scenes at four different points at once, and a single chapter status cannot say that. Stages are also yours to define, because no two writers agree on what the stages are — "needs a beta read" and "cut but keeping" are real stages for the people who use them and meaningless to everyone else.

Set a scene's stage by right-clicking it in the binder and picking one, or **Clear stage** to make it untriaged again. A scene with a stage shows a small coloured dot before its title; one without shows nothing, because "nobody has looked at this yet" is not the same as "outlined".

### Defining your own stages

**Settings → Scene stages** holds the list. A new project starts with five that mirror the chapter statuses, so nothing looks unfamiliar on the first day. Rename them, recolour them, add your own, or delete the lot and start over — clearing every stage puts the defaults back rather than leaving you with no way to stage anything.

Each stage has a **Counts as written** switch. Turn it off for a stage that holds notes rather than prose; words in those scenes then stay out of your totals. Outline ships with it off for exactly that reason: an outline placeholder counted as progress inflates every number you use to judge whether you are on track. A scene with no stage set does count — otherwise every project's totals would drop the moment stages arrived, for no reason the writer caused.

Removing a stage clears it from every scene that was at it. The scenes are untouched; they simply become untriaged again.

The [Dashboard](11-dashboard.md) breaks the book down by stage, beside the chapter-status breakdown.

## Keywords

Scene tags started as free text: a comma-separated list typed into the Inspector with nothing behind it. That makes "flashback", "Flashback" and "flash-back" three different tags, and correcting that meant opening every scene that used the wrong one.

**Settings → Keywords** holds the book's vocabulary. Each keyword has a name, a colour, and optionally a keyword it groups under — one level, because "Themes" over "grief" and "loss" is what makes a list of forty legible, and deeper nesting is a filing system nobody maintains. Each row also says how many scenes carry it.

**Collect from scenes** adds every tag already written on a scene to the list. Run it once on an existing project: without it the vocabulary starts empty, which is no use to whoever has the most tags. Spelling variants fold together as they arrive, which is the first clean-up the registry buys.

**Renaming a keyword renames it everywhere.** Type a new name and every scene tagged with the old one is rewritten in the same moment. Renaming onto a name already in the list is refused — two entries spelt the same are the exact problem this exists to prevent — and a scene that would end up tagged twice keeps one.

**Deleting a keyword takes it off the scenes too**, because retiring a word from the list while leaving it written on forty scenes is how a vocabulary drifts back out of control. Deleting a keyword that others group under brings those back to the top level rather than taking them with it.

Only tags **you** set are rewritten. Novalist's analysis suggests tags of its own; those are its reading of the scene, not your vocabulary, and renaming a keyword does not edit them. They become yours the moment you edit the tag list on that scene.

## Status workflow

A typical novelist workflow with the five built-in chapter statuses:

1. **Outline** — bullet points or rough structure; not yet written.
2. **First Draft** — first pass through, complete or near-complete.
3. **Revised** — restructuring, voice fixes, scene-level edits done.
4. **Edited** — line edits, prose polish, copy-edits applied.
5. **Final** — ready to export.

The [Dashboard](11-dashboard.md) shows a breakdown of chapters at each status. The [Manuscript](10-manuscript.md) view filter lets you read only chapters at a given status.

## Acts

Acts are simple named buckets (e.g. "Act 1: Setup", "Act 2: Confrontation") that group chapters.

To assign a chapter to an act, right-click it → **Rename Act** and type the act name (use the same spelling for every chapter of that act). An act header appears in the binder above the first chapter of each act, the [Timeline](12-timeline.md) shows acts as its broadest grouping, and the [Plot Grid](08-plot-grid.md) groups columns by act.

The Timeline's **Add structure...** dropdown can lay out a known story structure (Three-Act, Save the Cat, Hero's Journey, 7-Point) as timeline events to plot against — see [Timeline](12-timeline.md).

## Snapshots

Take a snapshot of the open scene from the toolbar **Snapshots** button (with an optional label such as "Before rewrite") and restore any earlier snapshot from the same list. Automatic snapshots are also taken before destructive operations such as Replace All. See [Snapshots](17-snapshots.md).

## Starting a scene from a template

The New Scene dialog offers **Start from** when the book has scene templates: pick one and the scene is born with its synopsis, prose skeleton, point of view, stage, label, tags and plotlines. Make a template by right-clicking a scene that already reads right and choosing **Save as scene template...**. See [Templates](07-templates.md#scene-templates).

## The chapter opener

The chapter dialog (right-click a chapter, **Rename chapter**) carries two settings that only show up in an exported book:

- **Subtitle under the chapter title** — a second line under the title. A novel uses it for a place and a date; a collection uses it for where the story first appeared.
- **Print this chapter with no heading** — the chapter opens straight into its prose. A prologue that begins without announcing itself is a real typographic choice, and this is how you make it, rather than leaving a chapter with a blank title.

The page still breaks before a chapter whose heading is hidden; only the words are gone.

Drop caps and small-capital lead-ins are set on the [export layout](20-export.md#export-layouts) rather than per chapter, because they belong to the edition rather than the chapter.

## Holding a scene back from exports

Right-click a scene in the binder and choose **Hold back from exports**. It stays exactly where it is, keeps its words and its place in every count, and simply never reaches a compiled book — for the scene you have written but are not sure belongs, or the one that is research in disguise. The same menu item lets it through again. See [Export](20-export.md#choosing-what-goes-in).

This is not archiving: an [archived](#archiving-scenes) scene leaves the binder, a held-back scene does not.

## Where to go next

- [Editor](05-editor.md) — formatting, split editor, comments, footnotes.
- [Plot Grid](08-plot-grid.md) — attach scenes to plotlines.
- [Calendar & in-world dates](13-calendar.md) — give scenes structured story dates.
- [Smart Lists](16-smart-lists.md) — saved scene queries.
