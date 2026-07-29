# Changelog

All notable user-facing changes to the Novalist desktop app.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Novalist
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Entries are grouped as
**Added**, **Changed**, **Fixed**, **Removed**, and **Security**.

This file covers the desktop app only. The iOS companion app is released separately under its
own `ios-*` tags and is not tracked here.

Changes land under **Unreleased**. When a tag is pushed, the release workflow uses that section as
the GitHub release notes and stamps it with the tag's version and date.

---

## [Unreleased]

### Added

- **Automatic backups** — Novalist now archives your whole project to a ZIP file on its own: when you open it, every 30 minutes while you work, and when you close it. Archives are kept outside the project folder, so losing the folder or a bad sync no longer takes the backups with it. Settings has a new Backups section for the interval, how many to keep, and where they go — point it at a synced or external drive and your work survives losing the machine.
- **Restore a backup from inside the app** — pick an archive from the list in Settings and press Restore. The current state is archived first, so restoring the wrong one can be undone. Your Git history is left untouched, because the `.git` folder is never archived in the first place.
- **Include the book cover** toggle on the Export view, for EPUB and PDF. On by default; turn it off for a submission manuscript.
- **More AI providers in the AI Assistant extension** — a new **Anthropic** provider that calls the API directly with your own key (its model list comes from the API, so new models appear without an extension update), plus an **Endpoint preset** drop-down that fills in the address for LM Studio, Ollama, OpenAI, OpenRouter, Groq, DeepSeek, Mistral, Together and xAI. Anything not on the list still works by typing its address in. Previously the only options were a hand-entered LM Studio address and the two command-line tools.
- **Send a manuscript out for editing and read the markup back in.** DOCX exports now carry your unresolved scene comments as real Word comments, anchored to the passage they belong to, so an editor sees them in Word's review pane. **Import editor's changes...** on the Export view then reads their returned file: comments can be attached to the open scene in one click, and every tracked insertion and deletion is listed with who made it. Tracked changes are shown rather than applied — Novalist did not lay out the file your editor worked in, so it will not rewrite your prose from it behind your back.
- **Export a map as an image** — **Export image** in the map toolbar writes the map to a PNG exactly as it appears, with a 1x / 2x / 4x scale for screen, e-book or print. Maps previously had no way out of Novalist at all, so a map could not reach endpapers, a cover designer, or the book itself.
- **Import an existing manuscript.** Novalist can now bring a book in from Word (.docx), OpenDocument (.odt), EPUB, Markdown, plain text and RTF — previously the only import path was a legacy Obsidian vault, which meant a writer with a book in Word could not get started at all. Novalist reads the file, works out where the chapters and scenes are from the heading styles the document actually carries (falling back to "Chapter N" lines only when a file has no headings), and shows you the full plan — format, chapter and scene counts, word count, per-chapter breakdown — before writing anything. Imported chapters are added to the end of the book, so nothing you already have is touched. Find it under **Import a manuscript...** in the backstage drawer.
- **Custom in-world calendars** — define your world's own months, month lengths, weekday names and year label from **Calendar setup** in the Calendar toolbar. Story durations are then counted in your world's time instead of being forced into twelve Gregorian months and a seven-day week. Write dates as year.month.day. The calendar model had shipped for some time but there was no way to edit it; now there is.
- **Front and back matter** — the pages around your story are now real, typed elements rather than chapters you fake: half title, title page, copyright, dedication, epigraph, table of contents, foreword, preface, prologue, epilogue, afterword, acknowledgments, about the author, also-by, and custom pages. Add them from the Export view. Each kind knows where it belongs, whether it carries a heading, and whether it belongs in the table of contents, so a dedication no longer arrives typeset like a chapter with the word "Dedication" over it. Pages can be reordered, held back from an export without deleting them, and moved between front and back.
- **Planning board** — a new view in the Plan group of the activity bar: an infinite surface for ideas that are not scenes yet. Drag loose cards anywhere, draw lines between them and label the lines with why they are connected. Keep as many boards as you like — one per act, one per subplot, one for the thing you have not worked out. Nothing on a board counts towards a word goal or appears in the binder until you press **Make this a scene**, which creates the scene with the card's text as its synopsis and leaves the card on the board pointing at it.
- **Writing sprints.** The smallest thing Novalist could count was a calendar day, so it could not tell you how many words you had written *this sitting* — the only figure that means anything while you are still in the chair. A timer in the status bar starts one: pick 10, 15, 25, 45 or 60 minutes (or no limit), and it shows the time left and the words added so far, live. Pause and resume without losing the clock. Finished sprints are kept with the project with a running total and an average pace, and the pace is only shown once a sprint has run half a minute, because below that the number says more about the arithmetic than about the writing.
- **Whole-book analytics on the Dashboard.** Novalist computed point of view and character mentions per scene and only ever showed them for the scene you had open, so "which character is this book actually about" and "have I forgotten this location since chapter two" had no answer anywhere. The Dashboard now charts POV across the book, scenes per act, and a strip per character and location with a cell per chapter — a gap in the middle of a strip is someone who leaves the book for six chapters, which is very hard to see any other way. It also lists what is in your Codex and never in the manuscript, which is the only place a character planned and quietly dropped becomes visible. Presence is counted by entity id from confirmed mentions, so two characters sharing a first name are never confused.
- **Export layouts you can author.** Novalist had four fixed presets and no way to change any of them, so a submission guideline asking for something slightly different had nowhere to go. Duplicate any of the four and edit the copy: body font and size, line spacing, margins, first-line indent, the space above a chapter title, the scene separator, how a chapter heading reads (`Chapter {{number}}: {{title}}`), whether scene titles are printed at all, and extra CSS appended to the EPUB stylesheet. The built-ins stay read-only on purpose — a layout named after a standard that no longer matches it is worse than no layout, and nothing would tell you. Layouts are stored with the book, since a novel and a short-story collection have no reason to share a list.
- **Locations, items, lore and custom entries can now change through the story.** Only characters could be restated at a chapter or a scene, so a city razed in act two could only be described as it is at the end — which meant reading the Codex in chapter three told you the ending. Any entry can now be restated at a point in the book: give the name, the description or any field as it stands from there, and everything you leave blank keeps reading from the entry itself. A scene restatement beats a chapter one, which beats an act one, so you can say "and by this scene, it is worse." While you are reading that part of the book the hover card shows the restated version, with the scope it came from and your note about why.
- **Story structure bound to the manuscript.** Applying Three-Act, Save the Cat, Hero's Journey or 7-Point used to append timeline events that by design never touched a chapter or a scene, so the structure and the book had no relationship at all. A new **Structure** panel in the Timeline lists every beat, lets you point it at the scene that fulfils it, and reports where that scene actually lands — measured in words, because "the midpoint" means halfway through the reading rather than halfway down the scene list. A beat sitting well off where the structure expects says so in as many words; unfilled beats are shown as the holes they are, and one click creates a placeholder scene for each of them.
- **Split a scene at the caret, and merge two scenes.** Right-click in the editor for **Split scene here**, or right-click a scene in the binder for **Merge with next**. There was no split or merge anywhere before, so doing either meant creating a scene, cutting, pasting, and then repairing order, date, plotlines, stage and overrides by hand — which is where the mistakes came from. A split carries the metadata that still describes both halves but deliberately not the synopsis, since that described the whole. A merge keeps the surviving scene's metadata, except that a synopsis only the second scene had is kept rather than lost, and plotlines are unioned because a merged scene genuinely serves both threads.
- **Scrivener project import.** Point the importer at a `.scriv` folder and Novalist reads its binder — folders become chapters, documents become scenes, synopsis cards become scene synopses. Scrivener 2 and Scrivener 3 layouts are both read. The preview names everything that will not come across (Research, Trash, Templates, labels, collections, snapshot history) before you commit, rather than leaving you to notice the gaps later, and your Scrivener project is never modified.
- **Slash commands in the editor, and writing from the caret.** Typing `/` at the start of an empty line opens a menu of extension actions that need no selection. Until now every inline action required highlighted text, so the two things a writer most often wants — carry on from where I stopped, and write towards this beat — had nowhere to be invoked from. With the AI Assistant installed, `/continue` writes the next 80–150 words picking up mid-flow from your last sentence, and `/beat she finally admits it` dramatizes that beat and stops there. Both read the prose before the caret so the continuation matches what came before, and both insert at the caret without replacing anything. The slash and what you typed after it are removed first, so you are left with the prose and not with the instruction.
- **Publishing metadata on the Export view** — ISBN, publisher, series and position in it, description, subjects, rights and publication date. An exported EPUB used to carry author, title, an identifier and a language and nothing else, so a retailer had no ISBN to key the book on and a trilogy had no way to say it was a trilogy — it shelved as three unrelated books. The ISBN becomes the file's package identifier, and the panel shows you the digits that will actually be written (or tells you that what you typed is not a usable ISBN) so you find out before ingestion rather than after. Series and position are stated the way EPUB expects, and both appear under the title on the title page.
- **Word targets on a scene, a chapter or an act.** Novalist had exactly two — daily and whole-project — and showed a bare word count in the binder with no subtotal anywhere. Now any scene, chapter or act can carry its own, set from the binder's right-click menu or typed straight down the Manuscript outliner's new **Target** column. A scene with one shows a small progress bar beside its count, which turns green rather than overflowing once you pass it. Targets **roll up**: a chapter with none of its own uses the sum of its scenes', and an act uses the sum of its chapters' — so setting targets on a handful of scenes already tells you where the chapter stands, without restating the same number at three levels.
- **Scene stages you define yourself.** A scene now carries its own revision stage, shown as a coloured dot in the binder and set by right-clicking it. Chapters had a status; scenes had nothing, which meant a chapter halfway through a revision pass — holding scenes at four different points at once — could only report one of them. **Settings → Scene stages** is where you name, colour and order your own, starting from five that mirror the chapter statuses so nothing looks unfamiliar on day one. Each stage says whether its words **count as written**, so an outline placeholder full of notes-to-self stops inflating the totals you use to judge whether you are on track. The Dashboard breaks the book down by stage.
- **You decide what AI sees of each Codex entry.** Every character, location, item, lore entry and custom entry now carries its own setting: sent when a scene mentions it (the default, and what Novalist did before), always sent, or never sent. Individual sections can be withheld on their own, so the section naming the killer stays back while the rest of the character still reaches the model. Novalist enforces this itself and hands an extension only the entries you allowed, with withheld sections already stripped — an extension has to go out of its way to see something you excluded. It affects only what is sent to a model: everything stays fully visible to you in the Codex, in exports and in search.
- **Paragraph styles and lists in the scene editor.** The toolbar now has a style drop-down — Body, Heading, Subheading, Block quote, Verse — and buttons for bulleted and numbered lists. Until now the scene toolbar was bold, italic, underline and four alignments; a heading could only exist in a scene if it arrived from an older version of Novalist, and block quotes and lists could not exist at all. Verse keeps the line breaks you typed and is never justified, so a poem stops being stretched across the measure.
- **Every export format now understands every style.** A heading becomes a real Word heading (so Word's navigation pane and table of contents find it) and a real `<h2>` in EPUB (so a reading system's navigation does); block quotes and verse arrive as themselves rather than as body text; and lists export as real Word numbering an editor can renumber, not as bullet characters typed into the sentence. Previously only Markdown and LaTeX honoured headings at all — DOCX and EPUB quietly flattened them into ordinary paragraphs on the way out.
- **A scene that changed somewhere else can no longer be overwritten silently.** Novalist keeps projects in plain folders, so people sync them and write on more than one machine — and until now, saving a scene wrote over whatever was in the file without looking, so the loser of that race lost their work with no way to find out. Saving a scene whose file changed since Novalist read it is now refused, and both versions are shown side by side: pick a side per line, or take one version wholesale. Prose is deliberately never merged automatically, because a sentence spliced from two drafts reads like neither. Choosing snapshots both versions first, so a wrong click is recoverable. **Decide later** changes nothing at all — your text stays in the editor, unsaved, and the file is untouched.
- **Language packs panel in Settings**, listing every language Novalist knows about and, separately, whether its interface translation and its scene-analysis word lists exist — because reading the menus in English while writing a French novel is normal, and one combined "supported" flag would be wrong for half the people asking. Every writing language the Quote Style picker offers is listed, so a language with no word lists is stated up front rather than discovered by wondering why your scenes have no detected emotion. A **Rescan** button picks up a language file you just dropped in without restarting, and beside any language with no word lists there is a button that writes a starting file for you, seeded with the English lists so the work is translating a real list instead of guessing the format.
- **Spell check that works offline.** Novalist now underlines misspellings as you type using the spell checker your operating system already has — no server, no account, no network. It is on by default, because an app that keeps your writing on your own machine should not need the internet to tell you that "recieve" is wrong. Right-click a red-underlined word for corrections, or to teach Novalist a name it keeps flagging; words you add are stored with your settings, so they follow you to another machine instead of having to be taught again. Checking follows the language you write in rather than the language the menus are in, and you can tick several dictionaries to check against more than one at once. The existing LanguageTool grammar check is unchanged and still optional — it catches what a spell checker cannot, but it needs a server, and until now it was the only proofing Novalist had.
- **Work on several scenes at once.** Ctrl-click (Cmd-click) scenes to build a selection and shift-click to extend it, in the binder, on corkboard cards, on outliner rows and on Calendar chips — one selection shared across all four, so you can pick scenes in the binder and act on them from the corkboard. A bar appears once two or more are selected: move them all into a chapter, tag them, archive them, delete them, or shift their in-world dates by a number of days. The date shift shows a before/after list of every selected scene first and uses your book's own calendar, so a shift across a month boundary lands where your calendar says it should. Dragging one selected scene in the binder carries the rest with it, and dragging one on the Calendar moves the whole sequence by the same number of days without collapsing the gaps between the scenes.
- **Per-entry detection rules in the Codex** — each entry now decides how its own name is picked up in your prose. Match only its exact capitalisation, so a character called Will stops raising his card on "she will go". Match the plural, so "Ravens" finds the Raven faction. List phrases that never count, so Rose is not detected in "rose garden". And silence an entry in a single scene from its hover card when it is simply not the right read there. Everything is off by default, so your existing books read exactly as they did.
- **Style report** — a new view in the Plan group of the activity bar that runs craft checks over a scene, a chapter, or the whole book: adverbs, filter words, weak verbs, passive voice, stock phrases, sticky sentences, repeated sentence openers, and sentence-length variation. Click any report to see examples in context. Everything is computed offline from your own text, so the same manuscript gives the same numbers every time, nothing is sent anywhere, and no AI is needed. Where your writing language has no word list for a check, Novalist says so rather than showing a zero that would read as clean prose.
- **Word targets on the Dashboard and in Settings.** A panel listing every act, chapter and scene target with its progress, where you can also set one - targets could previously only be reached by right-clicking in the binder, which meant most writers never found them. Settings -> Writing Goals now also carries the daily and project word goals, which until now could only be edited by clicking a Dashboard card's title.
- **Your own fields on scenes and chapters.** Settings -> Scene and chapter fields adds typed fields of your own to every scene or chapter of a book: text, a number, yes/no, a date, or one of a list you define. Fill them in from the scene notes dock, the Chapter dialog, or - for scene fields you mark for it - a column in the Manuscript outliner. Typed, so a tension of 7 sorts as a number rather than as the word "7", which is what tracking it through tags could never do.
- **Board mode in the Manuscript view.** Scene cards in columns, grouped by scene stage, chapter, POV, or any scene field of your own - and dragging a card into another column writes that field, so you rearrange the book by rearranging the cards. A "Not set" column always holds whatever you have not classified yet.
- **Premise, as project data.** The book in one line, then in one paragraph, then a summary per act, on the Dashboard - and a **Start from a premise** option in the new-project dialog that asks for the ladder and can lay out placeholder chapters under each act. The Snowflake-shaped setup wizard had been in Novalist for a long time with no way to reach it and nowhere for its answers to live.
- **Say who and what is in a scene.** An **In this scene** box in the scene notes dock assigns characters, locations, items and lore to a scene outright, and a star marks the one the scene is about. Presence used to be read only from @-mentions in the prose - so a character who is present and silent was invisible to the Wiki's appearances and the co-appearance stats, and there was no way to record what a scene was really about.
- **Timeline lanes.** A **Lanes** drop-down splits the timeline into one row per character, location, POV or plotline, each with that thread's events in reading order. The character and location filters could only ever narrow the timeline to one thread, which hides the threads you were comparing - lanes show them side by side, with anything unclassified in an Ungrouped lane that is never hidden.
- **Setups and payoffs.** A panel under the Plot Grid tracks what the book has promised the reader - the gun on the mantel, the letter she never opened - and which scene answers it. Promises nothing pays off sort to the top, and a payoff that has ended up *before* its setup, or whose scene was deleted, is called out rather than counted as kept. Until now Novalist had no link between two scenes at all.
- **An Inbox for every open note in the book.** A third tab in the context sidebar gathers every unresolved comment across the manuscript, with the scene it is in, what it was anchored to and who left it. Notes can be replied to, so a comment becomes a conversation; marked done and reopened; and flagged as a to-do, with a filter for just those. Until now a comment could only be found by reopening the scene it was left in.
- **A scene can say how it sits in time, and the Timeline can be read either way.** Mark a scene as a flashback, flash-forward, parallel, frame, dream or time skip in the scene notes dock - a parallel scene can also name the strand it runs on. The Timeline gains a **By date / In reading order** switch: reading order lays the book out as the reader meets it, with each scene's date and mode beside it, so a flashback stops sorting as though it happened at that point in the story.
- **Character arcs.** A character can record where they start, where they end, and the scenes that turn them - each turning point in your own words, and placed in a scene when you know which one it is. A **Character arcs** card on the Dashboard lays every arc out in reading order, which is where you see whether the turns are spread through the book or bunched into one stretch of it.
- **Read a chosen set of scenes as one text.** Select scenes anywhere and press **Read as one** in the bulk bar: the Manuscript view composes just those, in reading order, as continuous prose. Manuscript mode could only ever show the whole book or one chapter status, so reading a single POV's thread end to end was not possible.
- **Codex names are never spell-checked as mistakes.** Every character name, surname and alias, and every location, item and lore entry, is handed to the spell checker automatically - a secondary-world manuscript used to be a wall of red underlines despite the Codex holding every one of those names. They follow the Codex rather than being added to your dictionary, so renaming a character stops the old spelling being accepted.
- **An extension developer guide.** `docs/extension-guide.md` - referenced from the README and the manual and missing from the repository - now exists: the SDK surface, the hook interfaces, web views, storage, packaging, gallery submission, and an honest account of what the SDK will not let an extension do.
- **Eras on the in-world calendar, and a Timeline that reads it.** A custom calendar can now carry named eras, each starting at a year, so a date takes the era in force when it happens - and an era that **counts down** renders year -12 as *12 Before the Fall* rather than as a negative number. The Timeline groups a custom-calendar book by in-world year with the era's name; before this it tried to parse those dates as real ones and dropped every scene into the undated bucket.
- **Scene labels you name yourself.** Settings -> Scene labels defines the labels a scene can carry - a name and a colour each - and right-clicking a scene in the binder puts one on, on the whole selection at once when several are picked. A labelled scene's corkboard card is drawn with that colour. A label colour had been on the scene model for a long time with no name to give it and nothing that read it.
- **A tension curve on the Dashboard.** A bar per scene in reading order, as tall as that scene's intensity - the figure Novalist has estimated per scene for a long time and only ever shown as one number in the Inspector. A long flat stretch or a peak in the wrong place is visible here and nowhere else. Unrated scenes draw as a hairline, because "calm" and "not rated" are not the same thing.
- **Writing days, an adaptive daily goal, and a history worth reading.** Tick the weekdays you write on: days off are left out of every figure and never break a streak. Turn on **Recalculate today's goal from what is left** and the daily target becomes the words remaining spread over the writing days to your deadline, instead of the same flat number every day. The Dashboard also now reports longest streak, how often the goal was met, your best day, and your average per writing day - all from a per-day journal that was already being kept and shown only as a bar chart.
- **Story structures you can write, share and import.** The Structure panel's toolbar adds, edits, exports and imports structures - a name, a description, and beats with the point in the book each belongs at. Novalist shipped four in a hardcoded list with no way to add a fifth, so writing to any other method meant not using the feature at all. Editing a built-in saves your version under the same name, and deleting your version brings the original back.
- **Unlinked names.** A scan in the Codex lists every Codex name your prose uses as plain text - which entry, which scene, how many times, and the line it sits in - and links them all in one click. Only a real mention counts towards appearances and co-appearance figures, so an imported or hand-typed manuscript was under-reporting all of them and the only fix was retyping each name through the @-picker.
- **Strikethrough, highlighting and links in the editor.** Bold, italic and underline were the whole of inline formatting: a passage to come back to had nothing to mark it with, a cut line had to be deleted or left, and a reference could not be a link. Strikethrough is carried into DOCX, EPUB, Markdown and LaTeX exports - a struck line was meant to be seen struck. A highlight is a note to yourself, so the words export and the colour does not.
- **Your own flagged words in the style report.** A list of words you want counted - your own crutches, or the spelling a series bible fixes - reported as its own row beside Novalist's bundled checks. There was no local flagged-word list at all before this; "Add to dictionary" is a spell-check operation and has never had anything to do with craft.
- **Find reaches past the prose.** Two new options in Find and Replace: search each scene's synopsis, notes and comments, and search Codex entry names and section text. Those are exactly where a writer leaves the things they mean to come back to, and Find could not see any of them - a note reading "fix the bell here" was invisible to the only tool built for finding it. Results say which one a match came from. Replace All follows into synopses and notes, but never into comments or the Codex: a comment is a conversation, and renaming a Codex entry has its own command that carries the change through every reference to it.
- **Reading comfort you can set, and a High Contrast theme.** Themes here are colour only by design, so until now there was no way to open the lines up - the single most effective change there is for text that is hard to track. Line height, letter spacing and paragraph spacing are now settings, applied in the scene editor, in Manuscript mode and in the Expose. The new **High Contrast** theme puts pure white text on pure black with borders you can actually see, for low vision and for working in a bright room. Manuscript mode also finally uses your editor typeface instead of the page default.

### Changed

- **Focus mode is a composition mode now.** `Alt+F` hides the toolbar and the status bar as well as the two side panes, so the window belongs to the page. A new **Dim other paragraphs while writing** setting fades everything but the paragraph your caret is in, following it as you move.

- **Smart Lists are now rules rather than four fixed filters.** A saved list can hold as many rules as you like, over chapter status, act, POV, tag, plotline, scene stage, structure beat, title, synopsis, notes, words, word target, and any scene field of your own - and it can match **any** of them instead of all, which is what answers "either of these two POVs". Every field also offers **is set** / **is not set**, so "which scenes still have no synopsis" is a list you can save. Lists you already had keep working and become rules when you next save them.

- **The Export view asks what to export separately from what file to write.** "Codex (Markdown)" and "Codex (PDF)" used to sit in the format list as though a world bible were a file type. There is now a **What to export** drop-down - the manuscript or the codex - and the format list shows only the formats that make sense for it.
- **The Export view has one list of layouts instead of three ways to pick one.** The Preset drop-down now lists the built-in layouts alongside every layout you have authored, the Export layouts panel edits whichever one is picked there rather than carrying its own second drop-down, and the separate Shunn toggle is gone - Shunn Manuscript Format is in the same list as everything else.
- **Right-clicking a scene inside a multi-scene selection now acts on all of it.** Stage, archive, word target and delete apply to every selected scene, and each menu row says how many it will affect. Right-clicking outside a selection still acts on just that scene.
- **Planning boards can be deleted.** A board you no longer need can be removed from the board toolbar; the cards and connectors on it go with it.
- **The manuscript import dialog now lists the formats it can read**, so you know what to look for before opening the file picker.
- **Renaming a Codex entry now carries the new name everywhere it was referenced.** Mentions in your prose across every scene including archived ones, relationships on other entries, parent locations, manual POV settings, and `[[wiki-links]]` in any entry's sections all follow the rename in the same save. Mentions you edited by hand and the name written as ordinary prose are left alone — those are your words, not references.

### Removed

- **The Watch Filesystem project setting.** It has never done anything — no watcher was ever started — so a control that promised live reconciliation was leaving people to think external changes were being picked up while the app ran. External changes are still reconciled when you open a project, and a scene that changed on disk is now caught when you save it.

### Fixed

- **A struck or bold phrase no longer disappears from an export when it shares a paragraph with a span.** The export's tag matcher counted `<span>` as an opening `<s>`, and `<blockquote>` as a `<b>`, so it lost track of where the tag ended and dropped the text inside it.

- **Whole-project find and replace really does span every book.** The scope has always been advertised as "every scene in every book" and quietly searched only the open one. Results now say which book each match is in, and you are left in the book you started in.
- **Exported EPUBs no longer say "Title Page" in English regardless of the language you write in**, and each chapter file declares your book's language rather than `en`.

- **Footnotes are real notes in every export, not a block of text at the end of the scene.** DOCX now writes actual Word footnotes at the foot of the page, which Word renumbers for you; Markdown writes `[^n]` footnote syntax with the definitions at the end of the file; EPUB writes popup notes a reader can show in place; LaTeX writes `ootnote`; and PDF sets them as endnotes under their chapter. The number in your prose is a real reference now too - it used to be printed as a loose digit in the middle of the sentence.

- **Scene titles in the binder line up in a column again.** Giving a scene a stage pushed its title away from the left edge, so a list of scenes read as ragged rather than as a column - and the titles of scenes without a stage sat a few pixels off from the ones with. Every title now starts at the same place whether or not the scene has been staged.
- **Shift-click in the binder selects the whole run between two scenes again.** Clicking one scene and shift-clicking another selected only the second, because a plain click forgot where you had clicked.
- **A layout you authored yourself is now actually used by the export.** Picking a custom layout quietly fell back to the default, so the file came out in Default no matter what the drop-down said.
- **Import manuscript and Import review changes open again.** Both dialogs were rendering with no styling at all, which left them invisible - clicking the button appeared to do nothing.
- **The status bar is laid out correctly again.** Adding the sprint timer to it pushed the word count and the rest of the right-hand group out of position.
- **German interface text uses proper umlauts.** A batch of strings across maps, backups, editorial review, the canvas, front and back matter, calendars and manuscript import had been written with ue/oe/ae/ss spellings.
- **Imported RTF files no longer start with their font list.** Every RTF carries one, and the importer was reading it as prose — so a `.rtf` import arrived with "Times New Roman;" glued to the front of the first paragraph. Colour tables, stylesheets and document properties were leaking in the same way.
- **The @-mention picker no longer breaks the editor's selection handling while it is open.** A long-standing error thrown on every cursor move with the picker showing also stopped typewriter scrolling from re-centring until the picker closed.
- **Renaming a character no longer orphans everything pointing at them.** Relationships, parent locations and POV settings stored the old name and silently kept it, so a rename quietly broke the links between entries.
- **Your book cover now actually reaches the exported file.** EPUB and PDF exports had been silently dropping it despite the manual saying otherwise — the cover you set showed on the Dashboard and welcome screen but never in the book. EPUB now opens on the cover and registers it the way both retailers and Kindle expect; PDF puts it on a full page ahead of the title page, scaled without stretching.
- **Exported EPUBs no longer claim to be in English regardless of what you wrote.** The language is taken from your writing language, so a German or Chinese book is no longer mis-shelved when a shop ingests it.

---

## [2.5] - 2026-07-28

### Added

- **Custom themes** — write your own colour scheme and drop it into the Themes folder beside your settings, and it joins the theme picker in Settings, Appearance. Two formats work: a JSON file listing just the design tokens you want to change, where everything you leave out keeps its default, or a CSS stylesheet for a theme that needs rules a colour table cannot express. A stylesheet applies only while its theme is selected, so it can never bleed into another one. Type, spacing, and corner radii stay as Novalist sets them whichever palette you pick.
- **Language packs** — drop a JSON locale file into the Locales folder beside your settings and that language appears in Settings, Appearance, Interface Language under its own name. A partial translation is fine: anything it leaves out falls back to English, so a file with a handful of strings is usable straight away. A file whose code matches a language Novalist already ships patches that language instead of replacing it, so you can correct a term you dislike without maintaining a whole translation.
- **Word lists for more writing languages** — the Inspector's automatic emotion, intensity, conflict, and tag detection can now be taught a language Novalist does not ship, by dropping a word-list file into the Analysis folder beside your settings. A file there also overrides a bundled list, so you can extend the English, German, or Chinese lists with your own vocabulary. This also feeds the Dialogue view's speaker detection.
- **Themes folder and Languages folder buttons** in Settings, Appearance, which open those folders so you do not have to hunt for them.
- **Dialogue view** — a new view that gathers every line one character speaks across the whole book and lays it out in story order, so you can see whether their voice drifts over the course of the story. Lines are grouped by the scene's in-world date, with scenes that carry no date continuing the run before them rather than being pushed to the end, and each scene has a button to jump straight to it. Pick a speaker from the roster on the left, filter it by name, or open the Unassigned list to see the lines nobody could be traced to. Find it in the Plan group of the activity bar, after the Relationships graph.
- **Automatic speaker detection** — the Dialogue view works out who says each line from your prose alone, offline: an entity mention you placed in the dialogue tag, a speech verb beside a character's name, a second line continuing the same paragraph, a pronoun the surrounding narration can only mean one way, or back-and-forth alternation in a two-hander. It matches given names, full names, and every alias in the Codex, ignores names inside the quote marks (someone being spoken to is not the speaker), and recognises the quote styles for English, German, and Simplified Chinese, so a German manuscript in low quotes reads as well as an English one. Every line says how it was worked out — named, inferred, nearby, guessed, or unknown — so you always know which attributions to trust. A line nothing could be settled for is left unassigned rather than guessed at.
- **Pronoun tags** — a line tagged only "he said" or "brummte er" is credited to the character the narration names around it, but only when exactly one character of that gender is named there, so an ambiguous passage is never guessed at. Novalist looks at the paragraphs above the line and, for a scene that opens on a pronoun and names the character just after, the paragraphs below. This reads the Gender field on your characters, so filling it in makes attribution noticeably better.
- **Clickable speaker suggestions** — every line the prose does not name outright now offers its likely speakers with a percentage each, showing how the evidence splits between them. Click one to assign the line. Suggestions draw on who the narration names nearby and who has already spoken in the scene, so even a line with no dialogue tag at all usually puts the right name first.
- **Correcting who said what** — reassign any line to another character from the dropdown beside it. The list offers your whole cast, not just the characters who already have lines, so somebody whose every line was misattributed is still pickable. Your choice is kept, marked as yours, and survives later edits to the scene; picking Unassigned clears a wrong guess without naming a replacement.
- **Editing dialogue in place** — click a line in the Dialogue view to rewrite it, and the change is written straight into the scene file. You edit only the words inside the quote marks; the dialogue tag and the narration around it are left exactly as they were, and are shown greyed beside the line so you can tell two similar lines apart. A snapshot is taken before every edit, a line containing formatting is shown read-only rather than risking its markup, and an edit is refused with an offer to reload if you changed the same scene in the editor meanwhile.

### Fixed

- The **Override for this project** checkbox in Settings now shows whether the project actually overrides that section. It used to start unticked every time you opened Settings, even for a project that had its own appearance, editor, or writing settings — so the section looked global while the project's own values were still in force, and editing a field wrote to your global defaults while the control appeared not to change anything. The checkbox now reads what is stored with the project, so it is the same every visit.
- Ticking that checkbox now pins the section's current values to the project immediately, instead of only taking effect once you edited a field. Unticking still clears the section and returns it to your global values, and a line under the checkbox now says which of the two you are editing.
- Turning off an Appearance override that changed the interface language now tells extensions about the language change, so an extension's own labels revert along with the rest of the app.
- The cursor no longer jumps backwards to the end of the previous paragraph a few seconds after you click or arrow onto a line. It happened when the spelling and grammar check finished in the background: an empty line, or the very start of a line, was indistinguishable from the end of the paragraph above it, so restoring your position landed you one paragraph too far back. Your place on the line is now kept exactly, and a check that finishes after you have clicked away no longer pulls the cursor back into the text.

---

## [2.4] - 2026-07-28

### Added

- **Codex PDF export** — a new export format that writes your world bible as a single, self-contained PDF with entry images drawn into the document, so there is no image folder to send alongside it. Headings, bullet lists, bold text, and line breaks in your entry text are laid out as formatted prose. Every entry starts on its own page, and PDF bookmarks list each entry under its group so any reader can jump straight to a character.
- **Codex entry selection** — the Codex exports now show a checkbox per entry, grouped by Characters, Locations, Items, and Lore and sorted by name, with a search box, select-all and select-none buttons, and a running count. Untick the entries you do not want in the file; everything is included by default. Select all and select none apply to whatever the search currently shows, so you can tick or untick a group of entries in one click.
- **Bundled typefaces** — Fraunces, Newsreader, and Courier Prime now ship inside the app. The interface and the default writing face look identical on every machine, with no network and nothing to install, and all three are offered at the top of the editor's font list.
- **Formatted text fields** — Codex sections and per-scope overrides, entity long-text fields, research notes, timeline event descriptions, and wizard answers are now formatted as you write them: headings set larger, bold actually bold, list items bulleted, links and quotes picked out. The formatting marks are hidden, so a finished entry reads as clean prose; they reappear on whichever line your cursor is on, in case you want to edit them by hand. Each field carries a toolbar — bold, italic, strikethrough, heading, bulleted and numbered lists, quote, link — with `Ctrl+B` and `Ctrl+I`, so you never have to know the Markdown syntax to use it. What is saved is still plain Markdown, exactly as before: nothing is rewritten behind your back and existing entries open unchanged.

### Changed

- **Themes from extensions are picked in Settings now**, in the same dropdown as the built-in ones, instead of as a separate row of swatches in the Extensions view. They are full themes rather than an accent colour: an extension can supply a complete palette or a stylesheet, and its theme is remembered with the rest of your appearance settings, including per-project overrides. If you had an extension accent selected, pick the theme again in Settings, Appearance, Theme.
- **A new default look** — the Default theme is now Novalist's own identity: a deep, near-black page with parchment-coloured text and gilt accents. Panels are the one raised surface, edges are fine parchment hairlines instead of grey rules, and primary buttons carry a gold foil fill with a dark pressed-ink label. The active choice in a set of options fills the same way, and selecting text tints it gilt.
- Headings are set in Fraunces and interface text in Newsreader. Anything the app worked out for you — word counts, reading times, file sizes, version numbers, timestamps, branch names — is now set in Courier Prime, so figures read as the record they are and stay in step in a column.
- Interface text is larger and panels sit on more generous spacing, following the identity's own type, spacing, and corner-radius scale rather than the old compact one.
- New projects open the editor in Newsreader at 17px, instead of Inter at 14px. Projects you have already set up keep the font and size you chose.
- Long passages now open with a gilded drop cap — a manual page, or the opening description of a Wiki article.
- Choosing the Discord or Catppuccin Mocha theme now changes colour only. Type, spacing, and corner radii belong to the Novalist identity and stay put whichever palette you pick.
- Setting a custom accent colour flattens the gold foil on primary buttons to a single fill in your colour, rather than leaving a gradient that no longer matches.
- Readability scores in the status bar are drawn from the theme's own palette and follow whichever theme is active, instead of a fixed set of traffic-light colours.
- **Codex entry fields sit side by side** — short properties like Eye Colour, Role, and Build now flow into as many columns as the panel is wide instead of one per row, so an entry that used to run well past the bottom of the window fits on a single screen. Long text fields still take the full width.
- Novalist opens maximised. Restoring the window down still gives you the previous, smaller size.
- **Panels remember their size** — drag the binder, the inspector, or the scene-notes panel to the width or height you want and it is still there next time you open Novalist.
- The binder and inspector now open at a width proportional to your display instead of a fixed 240 and 280 pixels, so on a large screen a scene title fits on one line out of the box rather than being cut off. They can also be dragged considerably wider than before.
- Dialogs are wider, and update release notes have more room. The update notice used to break a version number across three lines.
- The welcome screen's toolbar now shows only the Novalist wordmark. It used to carry Add Chapter, Add Scene, search, snapshots, and the three panel toggles, none of which do anything before a project is open.
- On Windows and Linux the window no longer shows the grey system title bar and menu strip above the app. Novalist's own toolbar is the title bar, and the minimise, maximise, and close buttons are painted to match the theme — the same integrated look macOS already had. Press `Alt` for the File / Edit / Go / View menus.
- The focus peek card is larger, so an entry's sections, relationships, and description no longer read as a narrow column of fine print.
- The live statistics in the status bar sit in the true centre of the bar rather than drifting with whatever is beside them.
- Codex exports now write their group headings and fixed field labels (Role, Age, Type, Relationships, …) in your interface language instead of always in English.
- Characters whose age is set as a birth date no longer get an Age line in codex exports, where it only repeated the date.

### Fixed

- The focus peek card showed a Codex section's raw Markdown — `# Strengths` and `* Brave` as typed — where the Wiki has always rendered it. It now renders the same way.
- With typewriter scrolling on, right-clicking in the editor could dismiss the context menu the instant it opened: the click recentred the caret, and that scroll counted as scrolling away from the menu. The menu now stays put, and a right-click no longer moves the page under it.
- Text fields in the Codex, the outliner, and the notes panel now look like text fields before you click them. They previously showed no border or background until hovered, so a value read as plain text.
- Extension settings pages rendered as an unstyled stack of full-width controls with no spacing. Their fields now lay out in two columns inside a proper panel, matching the built-in Settings.
- The divider above the scene notes panel now highlights in the accent colour while you drag it, like the binder and inspector dividers already did.
- Dragging the scene notes panel taller now grows the Summary box as well; the extra height used to go to Notes alone while Summary stayed capped.

---

## [2.3.1] - 2026-07-26

Nothing yet.

---

## [2.3] - 2026-07-25

### Added

- **Exposé view** - a per-book pitch document with its own editor, reachable from the Publish
  group in the activity bar. Above the writing surface, two live counters show how many characters
  and how many Normseiten you have used. Set a character limit, a page limit, or both: the counter
  turns amber as you approach a limit and red once you pass it, but typing is never blocked. The
  exposé and its limits are stored with the book, so they are there the next time you open the
  project.
- **Paragraph styles in the Exposé** - Title, Section, and Body buttons above the editor mark the
  paragraph the caret is in (or every paragraph in the selection). The active button follows the
  caret, and styled paragraphs are drawn larger and bolder so the structure is visible while you
  write. Title and Section are what become upper-case headings in the export.
- **Export Normseiten** from the Exposé view - a DOCX laid out as German standard pages, ready to
  send to an agent or publisher. The exposé exports line for line: consecutive paragraphs stay on
  adjacent lines and only an empty paragraph opens a blank one, so what you laid out is what the
  page shows.
- **Normseiten export preset** for the manuscript - Courier New 12pt at exactly 20pt line spacing
  on A4, every line hard-wrapped at 60 characters and a page break forced every 30 lines, with a
  running header carrying the title and the page count. Because the pagination comes from the grid
  rather than from Word's reflow, the page count in the document is the count a lector will read
  off it. DOCX only.

### Changed

- Paragraphs carrying a heading style - imported projects can have them - are now drawn as headings
  in the editor instead of looking like ordinary text.

---

## [2.2] - 2026-07-23

### Added

- **Wiki view** - a read-only, encyclopedia-style article for every Codex entry, reachable from
  the World group in the activity bar. Each article has a lead summary with aliases, a table of
  contents, an infobox with an image gallery, your authored sections, and automatically derived
  cross-links: relationships, "referenced by", "appears with", plotlines, and map pins. Characters
  also get a "changes over time" section built from their chapter and scene overrides (including
  per-chapter portraits) and an Appearances timeline. Images open full size in a lightbox.
- **AI article summaries in the Wiki** (optional) - when an extension provides an article
  generator, articles gain an on-demand summary with a Generate / Regenerate button, a busy state,
  and an "out of date" chip once the entity has changed since the summary was written. Summaries
  are cached inside the project. Without such an extension installed, the Wiki simply shows no
  summary section.
- **Shared scene analysis** - a scene is now analysed once and every feature reads the same
  record: entity presence (present / mentioned / absent), per-character knowledge, and findings.
  Records are stored per scene and keyed by a content hash, so an unchanged scene is never
  re-analysed and project diffs stay clean.
- **Quick Open**, **quick capture**, and **global search** for jumping to scenes, chapters and
  Codex entries without leaving the keyboard.
- **Entity proposals** - analysis can now offer to create a Codex entry it found missing, instead
  of only reporting it. Extensions gained the API to create characters, locations, items, lore and
  custom entities.
- **Claude CLI** as an AI provider, plus opt-in background scene analysis.
- Localized scene-analysis wording for English, German and Simplified Chinese.
- A privacy notice shipped with the app.

### Changed

- Scrollbars, checkboxes, dropdowns, dropdown popups and text inputs now follow the active theme
  instead of being painted in the browser's light style over a dark UI. This includes the editor,
  manuscript and map panes, and it follows live theme switches.
- Git features report themselves as unavailable, rather than failing, on platforms where Novalist
  is not allowed to launch external processes.

### Fixed

- Your settings are now loaded when the app starts, not only the first time you open the Settings
  view.
- A project's language and theme override is applied when the project opens.

---

## [2.1.1] - 2026-07-21

Re-tagged build of 2.1 to republish the release artifacts. No functional changes.

## [2.1] - 2026-07-21

### Added

- macOS builds are now code-signed and notarized, so Gatekeeper accepts the DMG without a
  right-click-to-open workaround.
- A Mac App Store build (Apple Silicon).

### Changed

- The Mac App Store build hides the in-app update UI - updates arrive through the App Store.
- Tags with a prerelease suffix (for example `2.1-beta1`) are published as GitHub prereleases.

### Fixed

- The recent-projects list no longer breaks when a project's cover image cannot be read under the
  macOS sandbox.

## [2.1-beta1] - 2026-07-21 (prerelease)

### Added

- First cut of the signed and notarized macOS DMG plus the Mac App Store packaging pipeline.

---

## [2.0] - 2026-07-20

### Fixed

- The Windows installer.

## [2.0-preview1] - 2026-07-20 (prerelease)

**Novalist was rebuilt from the ground up.** The Avalonia desktop UI was replaced by a new
Electron shell backed by a headless .NET service, and the whole feature set was rebuilt on top of
it. Projects created with 1.x open unchanged.

### Added

- **New application shell** - three-pane layout with an activity bar, binder, editor, and
  inspector, a command palette, and a full hotkey system.
- **Editor** - formatting toolbar, live word and character counts in the status bar, page view,
  entity mentions with hover cards, focus mode, focus peek, split editor for two scenes side by
  side, comments, footnotes, and a grammar-check round trip.
- **Binder** - drag and drop to reorder chapters and scenes or move scenes between chapters,
  rename, delete, status cycling, act headers, and scene archiving with a restore browser.
- **Codex** - entity sidebar with grouping, a location tree, search, and move-to-World-Bible; a
  typed, grouped detail pane; create and delete entities; aliases, sections and relationships
  editing with autocomplete and automatic inverse sync; entity images; typed custom properties;
  character chapter and scene overrides; entities from book templates; custom entity types with a
  type manager; and a guided entity-creation wizard with a character interview.
- **Planning views** - Dashboard, Manuscript, Plot Grid, Smart Lists, Timeline (with zoom
  grouping, source and character/location filters, event chips, structure templates and outline
  export), Calendar (week / month / year, with drag-and-drop scene rescheduling), and the
  Relationships graph.
- **Maps**, **Image gallery**, and the **Research library**.
- **Export** in all seven formats, and **Git** with status, commit, push, pull and discard.
- **Find and replace** across scenes.
- **Snapshots** - take, list and restore from the inspector.
- **Scene analysis** in the inspector, alongside footnote and comment lists.
- **Settings** with scoped overrides (app-level and project-level) that propagate to the editor
  live.
- **Templates editor** covering known and custom fields, properties, sections and age mode.
- **Books and drafts** - switch, create and pick straight from the toolbar; project rename by
  double-clicking the title.
- **Writing goals** editing.
- **Obsidian vault import**.
- **New-project creation UI**.
- **Update notifications** with inline in-app updates.
- **Extensions** - a headless extension host and the SDK v2 webview contribution surface, with
  declarative settings schemas that support conditional field visibility, action buttons and
  field suggestions. The settings form auto-saves; the Save button is gone.
- Native Liquid Glass styling on macOS, and the theme set migrated to the new shell.

### Changed

- The manual and README were rewritten for the new UI, with screenshots, and the in-app manual
  viewer renders them.
- The status bar was decluttered.

### Removed

- The old Avalonia UI, and the SDK's Avalonia dependency. Extensions built against SDK 9 or
  earlier must be updated to SDK 10.

### Fixed

- Unreadable light theme under macOS glass (input surfaces are now opaque).
- Entity and gallery images failing to load.
- Book-relative image paths in Maps.
- macOS Gatekeeper rejecting the app (it is now ad-hoc signed).
- Installation on machines where the optional Liquid Glass module is unavailable.

---

## [1.14.5] - 2026-07-05

### Added

- Simplified Chinese (zh-CN) localization, contributed by the community.

### Fixed

- Inserting a LanguageTool suggestion into the scene.
- Character replacement and automatic dialogue punctuation.
- Scrolling when typewriter mode is switched off.

## [1.14.4] - 2026-06-17

### Fixed

- Several small bugs across the editor and project handling.

## [1.14.3] - 2026-06-17

### Added

- LanguageTool Premium support - use your own Premium account for grammar checking.

## [1.14.2] - 2026-05-25

### Fixed

- LanguageTool grammar-check issues and assorted small bugs.
- Documentation corrections.

## [1.14.1] - 2026-05-23

### Fixed

- Keyboard handling issues.
- Act assignment.
- Assorted bugs, plus documentation corrections.

## [1.14.0] - 2026-05-21

### Added

- **Opt-in diagnostic logging** (Settings -> Diagnostics) so a log can be sent when a problem
  cannot be reproduced. The log never contains your story text.
- **Project-level settings** - override app settings per project.

### Changed

- **New on-disk project layout (v3)**, with automatic migration when an older project is opened.
  Scenes carry front matter, and the app reconciles changes made to the files outside Novalist.

---

## [1.13.3] - 2026-05-18

### Fixed

- The WebKit install wizard on Linux.

## [1.13.2] - 2026-05-18

### Added

- Linux AppImage builds.
- Catppuccin theme.

### Fixed

- Linux build and WebView issues, plus UI polish.

## [1.13.1] - 2026-05-16

### Fixed

- The 3D map on macOS.

## [1.13.0] - 2026-05-16

### Added

- **Maps** - place and browse map pins, including a 3D map view.
- **Wizards** - a project outlining wizard (snowflake method), a guided entity-creation wizard,
  and a character interview. Extensions can contribute their own wizards.
- **Drafts** - keep multiple drafts of a book, clone from the current one, switch from the
  toolbar, and delete drafts you no longer need.
- **Scene archiving** with restore, a read-only archive preview, and an archive panel.
- **Typewriter scrolling** with a configurable anchor (top, middle, bottom).
- **Page view** - render the editor as a printed-book page with paper background, margins and
  shadow.
- **Aliases** for entities.
- **Writing streak and word history** on the dashboard.

### Changed

- Relationship type names are localized.

### Fixed

- Themes not applying.

---

## [1.12.0] - 2026-05-12

### Added

- A recent-activity feed on the dashboard, with timestamps.
- A busy/progress dialog for long-running operations, available to extensions through the SDK.

### Changed

- The SDK surface was expanded; the manual was updated.

### Fixed

- Assorted bugs.

## [1.11.0] - 2026-05-11

### Added

- **Calendar view** with in-world calendars and story dates, including a story-date-range dialog.
- **Relationships graph**.
- **Footnotes panel**.
- Favourites and per-item colours in the explorer.
- **Inline actions** - extensions can contribute actions that appear directly in the editor.
- The full user manual (`docs/manual/`).

### Fixed

- Focus peek.

## [1.10.0] - 2026-05-10

### Added

- **Snapshots** - take a snapshot of a scene before risky edits, list them, restore, and compare
  against the current text with line-level apply.
- **Find and replace** across scenes.
- **Command palette**.
- **Smart Lists** - saved, rule-based scene queries in the binder.
- **Plot Grid** - map plot threads against scenes.
- **Research library** - notes, links and imported files.
- **Comments** on selected text.
- **Scene notes and synopsis** panel.
- **Manuscript view modes** - manuscript, corkboard and outliner.
- **Export presets** and **project templates**.
- Point-of-view detection.
- Split editor toggle.

### Fixed

- Plot grid columns, the editor splitter, screen capture on macOS, and several macOS-specific
  issues.

## [1.9.0] - 2026-05-10

### Added

- Toast notifications.
- Editor tabs.
- Design tokens behind the theme system, and a UI polish pass across the dashboard, Codex hub,
  manuscript and timeline.
- Activity-bar contribution point in the SDK, so extensions can add their own top-level entries.

### Changed

- The AI assistant moved out of the core app into its own extension, which now registers itself
  through the SDK.

### Fixed

- Performance of project loading and the image gallery, timeline and manuscript views.

## [1.8.0] - 2026-05-03

### Added

- **AI-assisted grammar checking** - extensions can contribute a grammar checker, and the AI
  assistant ships one.
- More actions can be bound to hotkeys.

---

## [1.7.5] - 2026-04-30

### Added

- **Built-in grammar and style checking** with a configurable language checker.

### Changed

- Settings were reorganized into clearer sections.
- The update check moved to the splash screen, so it no longer interrupts you mid-session.

### Fixed

- Image paths.
- The image overlay is now scrollable.
- Context sidebar behaviour.

## [1.7.4] - 2026-04-22

### Fixed

- A crash in HTTP requests.

## [1.7.3] - 2026-04-22

### Fixed

- A crash on startup, and Avalonia was updated.

## [1.7.2] - 2026-04-22

### Added

- Renaming projects and books.

### Fixed

- Small bugs, plus documentation updates.

## [1.7.1] - 2026-04-16

### Fixed

- Hotkeys not working.

## [1.7] - 2026-04-12

### Added

- Codex hub quality-of-life improvements, including opening an entity's folder from the host.

---

## [1.6] - 2026-04-10

### Fixed

- Theme issues; Avalonia was updated.

## [1.5] - 2026-04-09

### Added

- **Custom entity types** - define your own Codex categories beyond characters, locations, items
  and lore, with an SDK example showing how extensions can use them.

### Fixed

- Small UI issues.

## [1.4] - 2026-04-08

### Added

- **Extension store** - browse and install extensions from inside the app.
- The SDK is published to NuGet, with a getting-started guide.

### Fixed

- macOS build and release-packaging issues.

## [1.3] - 2026-04-07

### Added

- Automatic dialogue punctuation correction.

### Fixed

- Automatic text replacement.

## [1.2] - 2026-04-06

### Fixed

- The macOS build on Apple Silicon.

## [1.1] - 2026-04-06

### Added

- In-app update checking and installation.

## [1.0] - 2026-04-06

First public release.

### Added

- Rich text editor with spellcheck.
- Books organized into chapters and scenes with status tracking (Outline, First Draft, Revised,
  Edited, Final) and scene metadata: point of view, emotion, intensity, conflict and tags.
- Scene notes panel beside the editor.
- Multi-book projects sharing one World Bible.
- **Codex / World Bible** - characters (with demographics, relationships, roles, groups, custom
  properties and per-chapter overrides), locations, items and lore, plus reusable templates per
  entity type and fast peek cards inside the editor.
- **Timeline** with manual events linked to chapters and scenes, categorized as plot, character or
  world events.
- **Dashboard** with word counts, daily progress and goal tracking.
- **Image gallery**.
- **AI assistant** extension with chat, story analysis (character and story consistency, scene
  statistics, revision suggestions) and project-aware prompt templating. Supports LM Studio and
  GitHub Copilot.
- **Export** to EPUB, DOCX, PDF and Markdown, with title-page customization.
- **Git integration** - branch status, ahead/behind counts, commit, push and pull.
- **Extension system** with the Novalist SDK.
- English and German localization.

---

[Unreleased]: https://github.com/Drommedhar/novalist-official/compare/v2.5...HEAD
[2.5]: https://github.com/Drommedhar/novalist-official/compare/v2.4...v2.5
[2.4]: https://github.com/Drommedhar/novalist-official/compare/v2.3.1...v2.4
[2.3.1]: https://github.com/Drommedhar/novalist-official/compare/v2.3...v2.3.1
[2.3]: https://github.com/Drommedhar/novalist-official/compare/v2.2...v2.3
[2.2]: https://github.com/Drommedhar/novalist-official/compare/v2.1...v2.2
[2.1.1]: https://github.com/Drommedhar/novalist-official/releases/tag/v2.1.1
[2.1]: https://github.com/Drommedhar/novalist-official/compare/v2.0...v2.1
[2.1-beta1]: https://github.com/Drommedhar/novalist-official/compare/v2.0...v2.1-beta1
[2.0]: https://github.com/Drommedhar/novalist-official/compare/v2.0-preview1...v2.0
[2.0-preview1]: https://github.com/Drommedhar/novalist-official/compare/v1.14.5...v2.0-preview1
[1.14.5]: https://github.com/Drommedhar/novalist-official/compare/v1.14.4...v1.14.5
[1.14.4]: https://github.com/Drommedhar/novalist-official/compare/v1.14.3...v1.14.4
[1.14.3]: https://github.com/Drommedhar/novalist-official/compare/v1.14.2...v1.14.3
[1.14.2]: https://github.com/Drommedhar/novalist-official/compare/v1.14.1...v1.14.2
[1.14.1]: https://github.com/Drommedhar/novalist-official/compare/v1.14.0...v1.14.1
[1.14.0]: https://github.com/Drommedhar/novalist-official/compare/v1.13.3...v1.14.0
[1.13.3]: https://github.com/Drommedhar/novalist-official/compare/v1.13.2...v1.13.3
[1.13.2]: https://github.com/Drommedhar/novalist-official/compare/v1.13.1...v1.13.2
[1.13.1]: https://github.com/Drommedhar/novalist-official/compare/v1.13.0...v1.13.1
[1.13.0]: https://github.com/Drommedhar/novalist-official/compare/v1.12.0...v1.13.0
[1.12.0]: https://github.com/Drommedhar/novalist-official/compare/v1.11.0...v1.12.0
[1.11.0]: https://github.com/Drommedhar/novalist-official/compare/v1.10.0...v1.11.0
[1.10.0]: https://github.com/Drommedhar/novalist-official/compare/v1.9.0...v1.10.0
[1.9.0]: https://github.com/Drommedhar/novalist-official/compare/v1.8.0...v1.9.0
[1.8.0]: https://github.com/Drommedhar/novalist-official/compare/v1.7.5...v1.8.0
[1.7.5]: https://github.com/Drommedhar/novalist-official/compare/v1.7.4...v1.7.5
[1.7.4]: https://github.com/Drommedhar/novalist-official/compare/v1.7.3...v1.7.4
[1.7.3]: https://github.com/Drommedhar/novalist-official/compare/v1.7.2...v1.7.3
[1.7.2]: https://github.com/Drommedhar/novalist-official/compare/v1.7.1...v1.7.2
[1.7.1]: https://github.com/Drommedhar/novalist-official/compare/v1.7...v1.7.1
[1.7]: https://github.com/Drommedhar/novalist-official/compare/v1.6...v1.7
[1.6]: https://github.com/Drommedhar/novalist-official/compare/v1.5...v1.6
[1.5]: https://github.com/Drommedhar/novalist-official/compare/v1.4...v1.5
[1.4]: https://github.com/Drommedhar/novalist-official/compare/v1.3...v1.4
[1.3]: https://github.com/Drommedhar/novalist-official/compare/v1.2...v1.3
[1.2]: https://github.com/Drommedhar/novalist-official/compare/v1.1...v1.2
[1.1]: https://github.com/Drommedhar/novalist-official/compare/v1.0...v1.1
[1.0]: https://github.com/Drommedhar/novalist-official/releases/tag/v1.0
