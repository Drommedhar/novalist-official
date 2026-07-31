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

- **Craft reference in the inspector.** Novalist's word lists were analysis stems shown to nobody — it could count filter words and could not help you find a better one. **Craft** now holds three things beside the scene you are writing: prompts for when the page is blank (including a set of moves for when you are stuck), a thesaurus of **specifics** rather than synonyms — the fear entry offers *a swallow that will not go down*, never the word *frightened* — and eight short pieces on point of view, stakes, scene shape, dialogue, description, revision, drafting and character. Prompts and thesaurus are in all three bundled languages; the articles are English for now and the panel says so.

- **Generate land on a map.** Every coastline, river and terrain polygon had to be drawn by hand from a blank canvas — the part of mapmaking that stops a writer who is not an illustrator. **Generate land** puts a first island on the open map: coastline, high ground, woods, rivers running to the sea, and settlements placed on land and never stacked on each other. It arrives on a layer of its own named for its seed, underneath anything you drew, and everything it makes is an ordinary shape you can drag, reshape or delete. The same seed makes the same land, so a coastline you liked can be made again.

- **A named rubric for reading a scene.** The per-scene analysis was descriptive — point of view, emotion, intensity — which says what a scene is and never whether it works. **Does this scene work?** in the inspector asks twelve named questions and, when you score one low, shows something to try. Leaving one unscored means *not asked here*, which is different from a low score: a chase scene is not failing at interiority, it is not trying. The Style report lists **Scenes worth another look** — everywhere you scored low, worst first — because what a revision needs is which scenes to open.

- **Languages view** — invented languages and their dictionaries, in the World group of the activity bar. Building a conlang used to mean hand-rolling a custom entity type, which gets you a list of entries and none of what a lexicon is for. Each word carries what it is, what it means, its part of speech and how it sounds, and words are added from a row of fields rather than a dialog because coining them happens in runs. The search reads both the word and its meaning, and shows matches from your other languages too — which is the one question the list in front of you cannot answer: did I already coin this somewhere else? Languages belong to the project, so a trilogy does not type its dictionary twice.

- **Spoiler control on what leaves the app.** A world page listing the villain's real name beside everything else is worse than no world page at all. Every Codex entry now has **What readers may see**: keep the whole entry from readers, or tick individual sections to withhold. Export the world or the Codex with **For readers** on and all of it is left out — a hidden entry does not appear at all, because listing the name and withholding the fields announces there is something to find. This is a separate setting from what an AI extension may see, so you can let a model know the twist while you plan without a reader finding it.

- **Plot threads and research notes keep their earlier versions.** Codex entries kept a history and nothing else did, so typing over a thread's description or pasting over a research note had no answer inside the app. Both now list **Earlier versions** with a **Restore** beside each, the same way a Codex entry does, and restoring keeps the current state as a version of its own. A save that changed nothing is not kept, so opening something and leaving it alone does not fill the list.

- **A family tree.** The Relationships canvas lays everything out by force, which is right for "what is connected to what" and wrong for "who descends from whom" — three generations read as a cloud. Centre on somebody and press **As tree** and the same people redraw as generations on lines, with separate limits for how many generations up and down to show and a switch between a tree that runs downwards and one that runs sideways.

- **Continuity checks over the whole book.** Every check Novalist ran was about the prose in one scene, so a character standing two chapters after their own funeral was nobody's job to notice. A **Continuity** section in the Style view now reports three things across the whole manuscript: somebody appearing after they have left the story, a scene casting an entry the Codex no longer has, and time running backwards with no flashback saying so. Every rule is deterministic — no model is asked and nothing is guessed from names — each can be turned off per project, and clicking a finding opens the scene. Codex entries can now be marked **gone from here** at a point in the story, which is what the first rule reads.

- **Timeline dates can follow other dates.** Every date stood on its own, so moving a siege by a week meant finding and retyping every date that hung off it — and the ones you missed said the wrong thing until somebody noticed the funeral happening before the death. **Follows this event** in the event editor hangs a date off another by a number of days, counted from that event's start or its end, and moving the anchor moves everything downstream through as many links as you have made. A span keeps its own length. **Pin this date** holds one event still while its own dependents still follow it. A loop, or an anchor on a date Novalist cannot read, leaves the dates alone rather than guessing.

- **A project can have more than one timeline.** Everything dated shared a single stream, so a war three hundred years before chapter one sat between two scenes of a Tuesday and the shape of the book disappeared under the backstory. **Add timeline** in the Timeline toolbar makes another, and a dropdown switches between them or shows all at once. An event can sit on several timelines rather than being copied into each, events you add while looking at one land on it, and removing a timeline keeps its events — they move back to the first one. Your chapters and scenes belong to the first timeline, so a backstory timeline shows only what you put on it.

- **An anthology's volumes can carry their own author.** A box set printed every volume under the project's single author, so a collection by six writers went out under one name. The Series view now has an author field per book, and it prints under that volume's heading in the export. Leave it empty and the book is by whoever wrote the project — repeating the same name over every volume of a series says nothing and reads as a mistake.

- **Backup and draft-comparison tooling can be an extension.** Extensions can take, list, read and restore scene snapshots — the same ones your snapshots dialog shows, rather than a second history beside them — read a draft you do not currently have open, and enumerate and read the project's own files. Comparing two drafts was the single most obvious reason to keep a second one and the one thing no extension could do. Restoring a snapshot is refused while that scene is open with unsaved changes. Research items can also resolve where their file actually is on disk.

- **Extensions can read the rest of your plan.** Plot-grid cell notes, your saved lists with the rules behind them, and your maps with their pins are now reachable, so an extension reporting on a book can respect a saved list rather than only ever covering all of it, and can say what a thread is doing in a scene rather than only that it is there. Cell notes can be written too; maps stay read-only.

- **Extensions can read and write what a scene and a chapter are.** An extension could read a chapter's title and its order and nothing else, so no extension could group a report by act, colour it by status, or place a chapter in story time. Chapters now report status, act, in-world date and date range, description, word target, running word count and your own fields, and an extension can set a chapter's status and change a scene's metadata a field at a time without disturbing the fields it said nothing about.

- **Extensions can create a project.** An importer bringing a book in from another program had no way to build the binder to put it in. It writes the project and tells you where; opening it stays your decision, so nothing can move you out of the book you are working in.

- **Extensions can work across books and drafts.** Everything an extension could do applied to whichever book and draft you had open, with no way to name another — so an importer could not build a second volume and a revision pass could not put its work in its own draft. Extensions can now list, add and rename books and drafts, start a draft as a copy of an existing one, and switch between them. Switching is refused while you have unsaved changes in the editor, because switching out from under an unsaved scene is how words go missing.

- **Extensions can fill in a Codex entry properly.** An extension could write an entry's name, description and notes and nothing else — so an importer brought a character across with their whole biography and not their hair colour, and a questionnaire had nowhere but a notes section to put the answers. Extensions can now write an entry's own fields, the properties you added yourself, and its relationships, with the other half of each relationship authored onto the entry it names.

- **An extension can no longer write over the scene you are typing in.** An extension that writes prose — an importer, a cleanup pass, a generator — could land on the open scene while you were working in it; its write and the editor's next autosave overwrote each other, whichever finished second won, and the words that lost were gone with no error anywhere. Novalist now tells extensions which scene is open with unsaved changes, and a write to it is refused. A pass over the whole book skips that one scene and keeps going through the rest.

- **Plot threads as lanes.** **As lanes** in the Plot Grid toolbar draws each thread as a track across the book instead of a row of ticks. A track runs from a thread's first scene to its last, so a gap reads as a gap; and every scene carrying more than one thread is marked down the full height. A matrix tells you which scenes a thread touches; this tells you where two threads meet, which is the scene doing the structural work.

- **A box set: several books in one file.** A multi-book project exported one book at a time — the compile only ever read the book you had open. **Also include these books** now appears above the chapter list, and each one you tick is appended after the open book with a heading carrying its name. Chapter numbering runs across the whole set rather than restarting per volume, and each volume sits a level above a chapter, so an EPUB contents nests its books instead of listing eighty chapters flat. Volumes are read without being opened, so building a box set never changes which book you are working in.

- **Codex entries keep their earlier versions.** Snapshots covered scenes and nothing else, so typing the wrong eye colour over the right one had no answer inside the app — the remedy was a backup of the whole project. Every change to an entry now keeps what it said before, listed under **Earlier versions** in the detail pane with a **Restore** beside each. Restoring keeps the current state as a version too, so an unwanted restore is undone the same way. The last 25 are kept per entry, beside the scene snapshots.

- **The whole project can leave in one piece.** Two new exports under **Data**: **Everything (JSON archive)** and **Everything (browsable page)**. As well as the scenes and the Codex, they carry plot threads with their steps, research notes with their tags, saved lists with the rules behind them, collections, and your map names — none of which had an export path at all. In a project with more than one book it carries the other books too, each with its own outline, threads and collections — read without opening them, so exporting never changes which book you are in. The page is a single self-contained HTML file, so it opens by double-clicking and cannot arrive with its stylesheet missing. Empty sections are printed with a count of zero, because "none" and "not exported" are different things.

- **The Relationships graph tells its kinds apart.** With people, places, things, knowledge and scenes all on one canvas, every node was the same rounded box. Each kind now has its own silhouette and outline colour — shape alone stops working at a distance and colour alone stops working once the graph is dense. Long names are cut to fit their box instead of running across their neighbours; hover for the whole thing.

- **Other programs can link to a place in your work.** Novalist now answers `novalist://` links: `novalist://open?project=<folder>`, optionally with a chapter and scene. A task in a tracker or a note in another app can point at the scene it is about. An already-running Novalist takes the link and comes to the front instead of a second copy opening the same folder. Links are read strictly — one that names no project, or a scene without its chapter, does nothing rather than opening something almost right.

- **Exports can be produced without opening the app.** The bundled backend now takes arguments: `--export <format> --project <dir> --out <file>`, optionally `--book`. An EPUB on every commit, or a fresh outline spreadsheet each morning, no longer needs somebody to click a save dialog. Exit codes are made to be read by a script, and a mistyped argument fails loudly rather than quietly starting a server nothing is talking to. See the manual page "Exporting from the command line".

- **Starting points for the entity types a world needs.** The custom type builder was an empty form, so everybody who wanted species, a magic system, factions or a language rebuilt the same field list by hand — and rebuilt it differently in every project. **Manage types** now offers five shipped packs: Species, Magic system, Faction, Language and Religion. A pack fills the builder in and gets out of the way; nothing is created until you confirm, and every field is yours to change first. Each field carries the question it is for, and that question stays on the entry instead of vanishing after creation.

- **The graph says how two people are related.** Novalist stored a tie as a role and a target — "mother", "Mira" — and could always draw the lines; nothing could say what they added up to. Centre the Relationships graph on somebody and every other node now carries what that person is to them: sibling, grandparent, great-aunt or uncle, second cousin once removed. You record parents once instead of recording every pair. Half-siblings who are also distant cousins read as siblings, because the nearest shared ancestor wins.

- **How thick the book will be.** The project overview popover now carries an estimated page count, per chapter and for the whole book. Novalist could only answer this exactly, and only through the Normseiten export preset — which is the right answer for a German submission and the wrong one for "how long is the paperback". Set **words to a printed page** per project in Settings: about 250 for a trade paperback, nearer 300 for mass-market, about 150 for large print. The popover says which figure it used, because a number with no stated assumption behind it is worse than no number.

- **A name generator, where the name is typed.** Naming is the most frequent thing that stops a draft mid-sentence, and Novalist offered nothing for it. The Codex create dialog now has **Need a name?**: pick a sound, drag from ordinary to unusual, and click a suggestion to fill the field — you still get to change it before anything is created. The sets are invented ones named for how they sound rather than for real cultures, because a handful of syllables cannot represent a real naming tradition and would get it wrong invisibly. Deterministic, so the same settings give the same list back and a name you liked and did not write down is reachable again. Entirely offline; no model involved.

- **Who drops out of the book.** Novalist has counted where every character appears for a while and only ever drawn it as a grid, plus "last seen N chapters ago" for one entry at a time in the Inspector — so finding the character who vanished in act two meant reading forty strips looking for a hole. A new Dashboard card reads them for you and lists the cast worst-first: how long each one is gone for and between which chapters, or how long it has been since they were last seen and where. Chapters are named rather than numbered. Arriving late never counts as disappearing, and one missing chapter is a scene rather than a problem.

- **Two more ways for the book to leave as data.** The Export view's **Data** contents already wrote a scene spreadsheet and a JSON document. **Codex sheet (CSV)** is the other half of the pair — the Codex as a spreadsheet, one row per field rather than per entry, so a character, a piece of lore and a type you invented yourself all fit the same four columns. **Outline (OPML)** carries the outline as a shape rather than a table — chapters as branches, scenes as leaves, synopses as notes — which is what Scrivener, OmniOutliner, Scapple and most mind-mappers read.

- **One Replace All is one batch of snapshots, and can be cleared as one.** A project-wide replace takes a snapshot of every scene it changes — hundreds at once on a long book — and they all carried the same label, so one run could not be told from the last and the only way to clear the clutter was deleting folders on disk with the project closed. Each run is now listed on its own under **Whole project**, with the number of snapshots it left behind, and clearing it spares every other run and everything you snapshotted yourself.

- **A plot thread is an object, not a row of ticks.** Right-click a thread in the Plot Grid and choose **Thread detail**. A thread can now say how much of the book it is (main, subplot or minor), whose story it is, and — the part a revision actually asks about — what has to happen for it to be finished, as an ordered list you tick off. The grid row itself shows the main-thread mark and a count of what is still open, so the thread that has gone quiet is visible without opening anything. No steps means nothing was planned rather than everything being done, and it says which.

- **A Large Print export layout.** 16pt Verdana with 1.6 line spacing, narrow margins and no first-line indent — a real large-print edition rather than a PDF somebody zoomed. Larger type needs proportionally more leading, not less; narrow outside margins because at 16pt a one-inch margin leaves too little measure for the lines to break well; and space between paragraphs instead of an indent, which is easier to follow at that size. Duplicate it and change anything your reader needs different.
- **Codex exports can leave parts of an entry behind.** Picking entries was all-or-nothing: a series bible carried every field, picture, relationship and section, or the entry did not go. Four toggles now choose which parts travel, and with Sections on you can tick section titles one at a time — so a reader-facing bible sends Appearance and History and holds back Secrets, without editing a single entry first. Untick everything and the export is names alone, which is what a submission packet's cast list is.
- **Settle a Codex entry so it cannot be changed by accident.** A world bible is a contract with the reader — once a character's eyes are brown in three published chapters, changing that field is a decision rather than a typo, and nothing stopped a stray keystroke in a detail pane from rewriting canon silently. Right-click an entry and choose **Settle this entry**: edits to its fields and appends to its sections are refused until you unsettle it. Works on every entity type, including your own.
- **A to-do list for the things that belong to no scene.** A todo comment is anchored to a passage and belongs to the scene it sits in — "read the whole thing aloud" and "decide whether Tomas survives" belong to neither, so they lived on paper. The Inspector's Inbox tab now carries **To do**, with named lists. A ticked item stays visible and struck through rather than disappearing, each list shows its progress, and **Untick the list** clears a whole checklist at once so a revision pass can be run again without retyping it.
- **A prologue is no longer Chapter One.** Novalist had one ladder — book, draft, chapter, scene — so a prologue was a chapter, was numbered as one, and pushed your first real chapter to Chapter Two. The only fix was hiding the heading and typing "Prologue" into the prose where no contents list could see it. A chapter now carries a **section type**: Chapter, Prologue, Epilogue, Interlude or Part. Numbering skips the unnumbered ones, the `<$chapternumber>` placeholder agrees with the heading, and an export layout can say how each type is set — so one draft compiles to a paperback, an ebook and a submission without editing the draft. Books with no types set export exactly as before.
- **Import a folder of ordinary Markdown notes.** Novalist could import exactly one thing: a vault made by the old Obsidian plugin, with its own metadata files. A folder of plain `.md` notes — what that vault becomes without the plugin, and what most other tools export — had no way in. **Import a folder** in Research reads front matter, falls back to the first heading and then the file name for a title, and turns the folders a note was filed in into tags. It scans and reports before importing anything, skips a tool's own state folders, and importing the same folder twice does not duplicate — so you can re-run it and only the new notes come in.
- **Submission tracking on the Dashboard.** Novalist produced submission-ready material and recorded nothing about where it went, so the one thing you must not do — send the same manuscript to the same agent twice — was the one thing it could not help with. Record who you sent to, what you sent, when, and what came back. Submissions still out are listed first and marked, dates are free text so a half-remembered "March" can be recorded, and if the book is already out with the name you are typing you get a reminder before you record it — a reminder, not a refusal, since re-querying on purpose is normal.
- **The Wiki can hold articles about the world, not just about the things in it.** Every article was generated from a Codex entry, so an essay on how the economy works or what the magic costs had to hang off whichever entry it least badly belonged to — or live in Research, outside the Wiki. **Articles** now sits at the top of the Wiki index: write one, nest it as deep as you like, and edit it in place. An article cannot be filed under itself or under one of its own children, and deleting one lifts its children into its place rather than taking them with it.
- **Cut a paragraph without losing it.** Deleted prose was recoverable only by opening a snapshot of the whole scene and reading it for the paragraph that used to be there — but a paragraph cut because it does not belong in this chapter is not a mistake to undo, it is writing looking for a different home. Select it, right-click, and choose **Cut and keep**: it leaves the scene and lands in **Cut and kept** in the Inspector's Inbox tab, with the scene it came from and the date. Search it, note why you kept it, copy it back out, or throw it away. It lives with the project, so it travels with the book.
- **The Style report now checks a scene against its own point of view.** Novalist has always recorded a POV per scene, and nothing ever read the prose against it — so a third-limited scene marked Mira could describe what Tomas was thinking with no warning. With a scene selected, the report lists every place the narration names somebody else and then reports what they are thinking. Aliases count. These are questions rather than errors, since omniscient narration does this on purpose, and when the check cannot run it says why instead of reporting a clean scene.
- **Sensory coverage in the Style report.** Five counts, one per sense — sight, sound, smell, taste, touch — shown apart from the other reports because they are not problems to reduce. The useful row is the one reading zero: nearly every writer defaults to sight and sound without noticing, so all five are always shown and a sense the prose never reached is drawn dashed rather than dropped. Deterministic and offline like the rest of the report, with word lists for English, German and Chinese.
- **Two reports compiled from what you already wrote.** Every scene carries a synopsis and a point of view, and neither could be read as a whole — the synopsis of a book existed only as forty separate boxes nobody could put side by side. Export now offers **Report**: a **synopsis of the book**, every scene's under its chapter in reading order, and a **point-of-view breakdown** showing scenes, words and share per POV. A scene with no synopsis is named and left blank rather than skipped, and scenes with no POV recorded get their own row — the gaps are the reason to read these.
- **A scene can point at another scene, a research note, or a Codex entry — and see what points back.** Research items could already reference each other both ways; scenes could reference nothing, so a scene that answers an earlier one could only say so as prose in its own notes, which nothing could follow and which the other end never knew about. The Inspector now shows **Points at** and **Pointed at by** for the open scene. Each link takes an optional reason in your own words, and a link whose target is deleted keeps its row and says so rather than vanishing.
- **The Gallery can be organised, and can import.** It searched filenames and nothing else, which is a poor index for a folder of four hundred references named whatever your browser called them. Pictures can now be filed into a **collection** and given **tags**, with a filter for each beside the search box, and **Import pictures** brings files in from disk without going through a Codex entry. Nothing moves on disk — a picture is already pointed at by scenes, entries, banners and map layers, so filing it into a real folder would break every one of those links. The filing lives with the project and travels with it.
- **A character arc can hold the want and the need.** Where a character starts and where they end say who they are on either side, but not what pulls them across. **What they want** and **What they need** now sit between the two, and one turning point can be ticked as **The turn** — the beat where the want gives way to the need. It is marked on a point rather than kept in a field of its own, so you can move it when you find out it lands in a different scene. The Dashboard's arc column draws the turn in the accent colour.
- **Placeholders now resolve everywhere you can type one.** They worked on front and back matter pages and nowhere else, and five of the ones the manual listed — chapter number, chapter numeral, chapter title, act and scene title — were filled in by nothing at all, so they came out blank wherever they were used. `<$wordcount>` and `<$pagecount>` printed a zero. All of them now resolve, in your prose as well as in matter pages, in chapter titles, and in a layout's chapter title format and scene separator. The chapter and scene ones resolve against the chapter and scene they sit in, and the word count covers only what is actually in the export. Placeholders resolve on the way out only — the saved scene keeps what you typed.
- **A layout can set the running head.** The line at the top of every page was fixed at surname and short title. A layout can now say what it should be, placeholders included, and leaving it empty keeps exactly what was printed before.
- **Clean up a whole manuscript in one pass.** Auto-replacements only fire while you type and deliberately skip pasted text, so a chapter written elsewhere and pasted in kept its straight quotes, double hyphens and double spaces for good — and putting that right meant running Find and Replace at each of them by hand. **Clean up the manuscript**, in the command palette, does the set: curls quotes in the pair your writing language actually uses, turns double hyphens and three dots into real glyphs, collapses repeated spaces, trims and drops empty paragraphs, and makes every scene break the same. An apostrophe is never mistaken for a closing quote. Preview it first to see how many scenes would change and which, and every scene it does change is snapshotted before it is touched.
- **Export the plan, not just the prose.** A new **Data (metadata)** choice writes your outline as a CSV spreadsheet or as JSON, so it can be opened in a spreadsheet or handed to another tool instead of being retyped. One row per scene with chapter, order, stage, POV, words, target, date, synopsis, goal, conflict, outcome, tags, plotlines and cast — plotlines and cast by name rather than by identifier. Unlike a compile, it keeps the scenes that stay out of the book and flags them, because those are the rows a planning sheet is for. JSON carries the Codex alongside the scenes.
- **Codex entries can hold files and links, not just pictures.** A recorded interview, a scanned deed, a clip of how a name is pronounced — all of it had to be filed as a Research item and linked back, stored somewhere other than the entry it was about. Every entry of every type now has **Files and links**: attach a file and it is copied into the project so it survives being zipped or moved, or paste a web address for a link. The kind is read from the file name, the same file attached twice is stored once, and removing an attachment leaves the file alone — another entry may be pointing at it.
- **A tour on the first run.** Novalist has eighteen views behind four groups in the activity bar, and a writer at a blank Dashboard had no way to know the Plot Grid or the Codex were there at all. A card in the corner now offers a seven-stop walk — and each stop actually goes to the view rather than describing it. Skip is as prominent as Next, it is offered once per installation, and **Ctrl+Alt+T** brings it back whenever you want it.
- **One filter, shared by the Manuscript and the Timeline, and saveable.** The status filter in one and the character and place filters in the other were unrelated controls: narrowing to one thread meant setting it again in each view, and it was gone the moment you navigated away. A single filter bar now narrows by status, character, place, thread or stage, stays set as you move between views, and can be named and kept — so "Mira's thread, final draft only" is one click next time.
- **Bookmarks show what they point at.** A bookmark that only navigates makes you go and look to remember why you kept it — thirty trips for a list of thirty. The chevron beside one now opens a few lines of the passage, the chapter's opening, or what the Codex entry is. Scenes are read only when you open a preview, so a long list costs nothing until you ask, and a bookmark whose sentence has since been rewritten shows the scene's opening rather than nothing.
- **Edit the whole Codex in a table.** Entries were edited one form at a time, which is right for writing a character and wrong for filing forty of them — comparing two meant remembering the first, and putting a house on each of forty meant forty trips through the detail pane. **Table** in the Codex type bar gives you every entry of the type as an editable grid: name, role or description, the place it sits inside, and its group. Enter saves a cell, Escape puts it back.
- **Export a build for one shop.** A book sold in five stores left with the same back-matter link in every copy, sending four of those readers to a competitor — and Amazon refuses a book whose back matter links to a rival. Record where the book can be bought in the publishing panel, write your back matter with `<$storename>` and `<$storelink>`, and pick a store in **Build for**: each build points its reader back where they bought it. No store picked leaves both empty, which is what a build for your own site wants.
- **The book can complete its own words.** The only completion was the @-mention picker over Codex names, in scene prose and nowhere else — so every coined word, rank and settled spelling got retyped, slightly differently each time, and the inconsistency turned up in copy-edit. Settings → Word completion holds a list the book carries; type three characters of an entry anywhere you write and Tab accepts the rest. **Add every Codex name** fills it from your cast, places, items and lore in one press. Enter is deliberately left alone — in prose it starts a paragraph.
- **Pick the model at the moment you generate.** It lived in the AI Assistant's settings form and nowhere else, so trying a heavier model for one hard paragraph meant opening Settings, changing it, generating and changing it back. A picker sits beside Preview in the chat, listing what your provider can actually serve. The choice lasts for the session and clears when you change the model in Settings.
- **See what the AI is about to be told, before it is sent.** The prompt was assembled and sent in one step and nothing showed what went, so ticking six characters and getting an answer that ignored two had no explanation. **Preview** in the AI Assistant's chat lists every block that would go, what kind it is, roughly what it costs in tokens, and whether it fitted in the budget — with the assembled prompt itself underneath. Nothing is spent to look.
- **A Statistics report in the Insight extension.** Chapters, scenes, words, characters with and without spaces, and a printed-page estimate — the only exact page count Novalist could give you before came out of the Normseiten DOCX export, so "how long is this book, in the shape it will be printed" meant exporting to find out. Pages are estimated both ways, by words per page and by characters per page, because a word count says less about set length in some languages than others; both figures are yours to set. Parked scenes are left out unless you ask for them.
- **The place tree can be dragged, and has worlds at the top of it.** Reparenting a location meant editing an autocomplete field by hand — fine for one change, miserable for reorganising a continent. Drag a place onto another to move it inside; drop it on the strip above the list to lift it back out. Dropping a place inside its own subtree is refused, because a place that is its own ancestor has no root and the whole branch would silently disappear. **Mark as a world** puts a place at the top of the tree as the container everything else sits in — which is what a project with two settings needs, since without it two worlds are two unrelated piles of places with nothing saying which is which.
- **Your Themes, Locales and Analysis folders are watched.** All three were read once at startup, so iterating on a theme meant a relaunch per edit — the wrong loop for something you tune by eye. Add, edit or delete a file and it is picked up within a moment, with the window repainting live if the theme you are editing is the one you have selected. Only `.json` and `.css` files count, so a note or a screenshot in the folder costs nothing. Rescan is still there for a network folder the watch cannot start on.
- **Theme tokens can be edited in the app.** Appearance offered a theme, an accent colour and two folder buttons; changing anything else — a darker surface, a larger body size, squarer corners — meant hand-writing a JSON token map or a `.css` file and restarting to see it. Settings → Theme tokens edits them directly, grouped by what they do, applying as you make each change. Overrides sit on top of whichever theme is selected, so trying another palette does not lose them. The reset arrow puts one token back; the Profile box is the whole set as JSON, to keep, to paste back, or to send to somebody else.
- **Groups and factions are more than a typed word.** A group was a bare string on each Codex entry: it could say a house and a ship both belong to the Ravens and nothing else. Settings → Groups and factions gives each one a colour, a description and a count of everything in it across every entry type. **Collect from the Codex** picks up what you already use, folding spelling variants together. Renaming a group rewrites every entry in it in one move — correcting "the Ravens" to "House Raven" no longer means opening the character, the ship, the port and the ledger one at a time — and deleting one takes it off those entries too.
- **Map pins can be drawn as symbols, not only dots.** Eighteen shapes — city, town, village, castle, tower, ruin, temple, mountain, hills, forest, water, port, bridge, mine, camp, crossroads, cave, battle — so a capital no longer looks exactly like a campsite and telling them apart no longer means reading every label. A shaped pin with no label is a map symbol: a row of mountains along a range, a wood over a forest. Shapes take the pin's own colour, are drawn as outlines so they stay legible over painted terrain, and stay sharp at any zoom.
- **Collections — scene sets you gather by hand.** A Smart List is a query and recomputes every time you open it. A collection is the eight scenes to fix before Tuesday, or the run you are reading to your writing group: nothing they have in common is expressible as a filter, which is why they had to be picked one at a time. Select scenes, name a set, and it is made. Scenes stay in the order you added them rather than reading order, a scene can be in as many collections as you like, and removing one from a collection or deleting the collection never touches the writing.
- **A name generator in the Toolkit extension.** Naming is the thing that stops a sentence most often, and Novalist had no help for it at all. The Toolkit board has a Names tab with data for eight sound families — English, Germanic, Norse, Slavic, Romance, Celtic, Arabic and Japanese — filters for gender, what a name starts with, ends with or contains, and a choice between real names and invented ones. Invented names are built from the culture's own list, so they sound right without being names anyone already has. Everything runs offline; click a name to copy it.
- **Named workspace layouts.** Novalist always opened in the same shape, so planning, drafting and revising meant dragging the same panels back and forth several times a day. Ctrl+Alt+L saves the shape you are in under a name — view, tabs, panel visibility, widths, focus mode — and brings it back with one click.
- **The binder can be reordered and pinned.** Order the scenes inside each chapter by reading order, title, word count or stage, and narrow the whole tree to one plot thread. Right-click a scene to pin it to a group above the book, with its chapter beside it; pins survive restarts. Dragging to reorder stays available only in reading order, so a drag can never write a reorder you did not mean.
- **Corkboard cards can be coloured by what you choose.** The band down a card's edge can now mean the scene's label, its viewpoint, its act, or its chapter's status. Apart from labels the colours are worked out from the values themselves, so the same viewpoint is always the same colour and nothing needs setting up first.
- **The EPUB table of contents can be configured.** It was flat, chapters-only, English-titled and had no setting anywhere. Export now offers a contents depth — chapters, or chapters with their scenes nested underneath — and a contents heading you can name yourself, so a German book gets "Inhalt" rather than "Table of Contents". Titled scenes are listed whether or not the layout prints their titles; untitled ones are skipped, because "Scene 3" is not navigation.
- **DOCX export can borrow a publisher's styles.** Point the new Reference document field at the styled Word file an agent or publisher sent you and the export comes out in their house style, instead of having to reapply it by hand every single time. A reference file that is missing or unreadable is ignored and the export still runs.
- **A scene can say "two hours later" instead of naming a date.** Novalist stored absolute dates and nothing else, so a writer who knows a scene follows the last one by an afternoon — and neither knows nor cares which afternoon — had to invent a date or leave it blank, and blank dropped the scene out of the Calendar and the Timeline. Set an amount and a unit in the scene-notes dock and Novalist works the date out. A date you typed always wins, offsets accumulate down the book, and negative offsets are allowed because that is what a cut-back is. Scenes before the first real date stay unanchored rather than being hung off an invented starting point.
- **Plot threads are more than a row of ticks.** A plotline now carries an **importance** (main, subplot or minor), the **cast** it belongs to, and an ordered list of **steps** — what has to happen for it to be finished, each tickable and each able to point at the scene where it lands. A grid of equal rows says a romance running through every chapter and a running joke are the same kind of thing, and it can say which scenes a thread touches but never whether the thread ever resolves, which is the commonest developmental note there is.
- **Thread colours in the binder.** A plotline has had a colour since the Plot Grid shipped and it never left that view. Scenes now show a small dot per thread, so the fact that this scene and that one are the same thread is visible where you write rather than only in the grid.
- **Insert a chapter in the middle of the book.** Right-click a chapter → insert before or after, and everything from there on moves down. Until now the only way was to create the chapter at the end and drag it up past everything after it, which on a long book is a dozen drags and a dozen saves. Chapter folder names on disk keep their original numbers, because renaming them would break every snapshot path and every open editor for a cosmetic gain.
- **A chapter can say what it is for.** Right-click → **What this chapter is for...** stores a note in your own words that is never printed — which is what separates it from the subtitle, the line a reader actually sees under the chapter title.
- **The Insight extension can tell you who drops out of the book.** A new report lists every character with the chapters they appear in, the longest stretch they are absent for and where it starts, and flags the ones who vanish for most of the book or never reach the page at all. The Inspector's "last seen N chapters ago" answers whether the character in the open scene is overdue right now; it cannot answer who disappeared from act two. Gaps are counted only between a character's first appearance and their last, because arriving in chapter twenty is not an absence from the first nineteen.
- **Bookmarks.** A third tab in the binder for places worth coming back to. A saved list answers "which scenes match this query"; a bookmark answers a different one — the paragraph where she finds out, the entry you keep re-reading, the day the siege starts — and had nowhere to live, so people kept them in a scene called Notes. Right-click a scene to bookmark it. Bookmarks can also point at a chapter, a Codex entry, research, a story date or a map, and can be gathered into named groups. A bookmark on a passage stores the text rather than a position, because prose above a mark is edited constantly and a position would drift into an unrelated sentence.
- **An Accessibility section in Settings.** Editor typeface, font size and line spacing gathered where somebody looking for them would look, plus one click to the High Contrast theme. The typeface list now names faces chosen for readability — OpenDyslexic, Atkinson Hyperlegible, Lexend — instead of leaving you to know what to type into a free-text box. Novalist does not bundle them, and the hint says so.
- **The Style report can measure narration and dialogue separately.** A drop-down chooses everything, narration only, or dialogue only. A character written to speak in cliches is not a writing problem, and a report that counts their lines alongside your narration says otherwise — which is the usual complaint about tools of this kind. Novalist has segmented dialogue precisely for a long time and never used it here.
- **Paragraph shape in the Style report.** Paragraph count, mean paragraph length, and paragraph variation sit beside the sentence figures. Sentence variation is the well-known one; a chapter of identically-sized paragraphs reads as flat for exactly the same reason and is just as invisible while you are writing it.
- **Replacements that change the exported file and never your prose.** A new panel on the Export view holds ordered find-and-replace rules, plain text or regex, applied every time you export. Find and Replace rewrites the source scenes and snapshots each one — right for fixing a name, wrong for "the submission copy spells it out and the ebook uses the glyph". These run on the way out, so turning one off leaves nothing to undo, and a rule you need for one submission and not the next can be switched off instead of deleted.
- **Placeholders in front and back matter.** `<$title>`, `<$author>`, `<$isbn>`, `<$series>`, `<$chapternumber>`, `<$chapterroman>` and a dozen more resolve from the book when the export runs, so a title page reading "book two of the Salt Road" stays right when the series position changes instead of being typed out and forgotten. A placeholder Novalist does not know is left exactly as written — silently deleting something you typed is worse than printing it.
- **Maps have a scale, a ruler and a grid.** A map drew in its own units with a zoom readout beside them and nothing that said what a unit was worth, so "how many days' ride to the coast" could not be answered from a map Novalist itself drew. Set the ground distance per map unit and what to call it, and the map gets a scale bar showing a round distance at the current zoom. **Measure** takes two clicks and answers in your unit — and says so plainly when you have not set one, because a number with no unit behind it is the problem. An optional grid draws at a spacing you choose. All of it works while reading a map, not only while editing one.
- **A pin can open another map.** Point a pin at any other map in the project and clicking it in view mode goes there. A world map marks a city and the city has its own map — that is the one relationship maps really have, and Novalist kept them in a flat list of tabs with no way to say it.
- **Timeline events can say who was there and where.** Novalist has stored both on an event for a long time and only scene analysis ever filled them in, so backstory that never happens in a scene could not be attached to the people it defines. The event editor now asks for both, and names that resolve to a Codex entry become links.
- **Spans on the timeline.** Give an event an end date — or a scene a date range — and it draws a bar under its date showing how much of the story it covers. Every bar is measured against the whole book, because two bars only mean anything next to each other if they share a scale: a war spanning ten chapters and a pregnancy spanning twenty are now comparable at a glance, and where they overlap is visible. Duration used to be printed as "3 weeks" beside a marker dot, which says how long something took and nothing about what it runs alongside.
- **The Relationships graph can show one entry's neighbourhood instead of the whole world.** **Centre on** picks an entry and **How far out** sets one to four hops from it. A whole Codex on one canvas proves the links exist and answers nothing; the question you actually have is what this one is connected to, and two hops is usually where a family or a faction becomes a visible shape. Clicking a node recentres on it, so following a thread no longer means leaving the view and losing the shape you were reading — Alt-click still opens the article.
- **Scenes on the Relationships graph.** **Show scenes** adds a node per scene edged to everything in it. Novalist has always known which entries appear in which scene and never drew that edge, so the graph could tell you two characters are siblings but not that they are in nine scenes together — and "where do these two actually meet" had no answer. Clicking a scene node opens the scene.
- **Groups span every Codex type.** A group was a plain field on characters alone, which cannot say that the captain, the ship, the port and the ledger all belong to the same faction — and a faction is exactly the thing that spans types. Every entry now has one, and the box offers the group names your project already uses so the same faction is not spelled two different ways.
- **Guiding questions on template fields.** A custom field can carry one line saying what belongs in it, shown under the field on every entry of that type. The guided wizards already asked the question during creation and then it vanished — which is exactly when somebody comes back to fill the field in and no longer remembers what it was for.
- **A scratchpad that does not need a project open.** Quick capture wrote into the open project's research inbox, so a thought arriving before the right project was open had nowhere to go — which is exactly when thoughts arrive. Press the quick-capture shortcut with no project open and it goes to a scratchpad instead, kept beside your settings, shown on the welcome screen, surviving every project being closed. Open a project and **File into project** moves a note into its inbox, where it can be filed like any other capture.
- **Research items have a status, a rating and links to each other.** A shelf of forty sources had three that mattered and nothing said which, and nothing separated a question still open from one settled three months ago. Each item now carries a status (open question, looking into it, answered), a one-to-five rating, and links to other research items. Links are written both ways, because the end worth finding is usually the other one — the question a source answers is what you are reading when you need the source.
- **The book can say what it is written in, and a scene that drifts is pointed out.** Two drop-downs on the Dashboard's Premise card declare the narrative person and the tense the book is meant to be in. Novalist has detected a point of view per scene for a long time, but nothing said what the book as a whole was supposed to be — so a first-person novel with one third-person scene in it had nothing to be wrong against. Set them and the Inspector says when the open scene reads differently. It stays quiet where it cannot be sure: under about sixty words there is not enough prose to read, a weak reading is phrased as a question rather than a verdict, and tense is checked only for languages that mark it with verb forms (English and German, not Chinese).
- **The pitch, stored with the book.** Genre, readership, comparable titles, setting, a blurb and a synopsis, on the Premise card under the act summaries. Every one of these is asked for by name on a query letter, a submission form or a retailer page, and every one of them used to live in a document somewhere outside Novalist — which is how a comparable title ends up quoted from memory and a genre gets described three different ways in three different submissions. The blurb and the synopsis are both there on purpose: one withholds the ending, the other gives it away, and you need both.
- **Weekly and monthly word goals.** Novalist had a daily goal and a whole-project goal and nothing between them, which is the wrong shape for anyone who writes a few heavy days rather than a little every day — you miss four days in seven while being exactly on schedule, and a bad Tuesday can never be made up on Saturday. Set either in Settings → Writing Goals and it appears under the daily bar on the Dashboard. Both are off until you set one. Where you are behind, the Dashboard says what it would take: the words left over the writing days left in that week or month, so "behind" reads as "behind, with three days to fix it".
- **A scene can say what it wanted and what came of it.** Goal and outcome are two new fields in the scene-notes dock and two new columns in the Manuscript outliner. Neither is ever guessed from your prose — conflict can be read out of a scene, but a goal nobody stated and an outcome nobody wrote down are exactly what a draft is missing. Read the two columns down the outliner and a scene where nothing happens says so: the outcome repeats the goal, or there is no outcome at all. Smart Lists can ask about both, so "every scene nothing has come of yet" is a list you can save.
- **Take a scene out of the book without archiving it.** Right-click a scene → **Take out of the book**. It keeps its place in the binder, the corkboard, the outliner and the Plot Grid, and leaves the manuscript: its words stop counting towards any total or target, and every export skips it. Archiving was the only step down from keeping a scene, and an archived scene disappears from every planning view — so a scene you were still deciding about had to be either fully in or invisible. Three chips above the binder choose what it shows: what is in the book, everything, or just the ones you have parked.

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
- **The editor reads your scene back to you.** A speaker button on the toolbar starts at the paragraph your caret is in and reads on, highlighting the sentence it is speaking and scrolling to keep it in view - so you can follow it with your eyes, which is what catches a sentence that does not land. Stop it with the button, with Escape, or just by typing. Speed and voice are in Settings; left on the default, Novalist asks for a voice in the language the scene is written in, so a German scene is read in German. It uses the voices your system already has and sends nothing anywhere. The highlight never touches the document, so listening to a chapter does not mark it as edited.
- **Sentence-level readability in the editor.** Novalist graded a whole scene and nothing smaller, so "this scene reads hard" never told you which sentence. The gauge button on the toolbar now tints the sentences that fight the reader - a light wash for difficult, a stronger one for very difficult - graded one at a time with the same method the style report uses for your writing language. Everything easier is deliberately left alone: tinting every sentence produces a heat map you stop seeing. Short lines are never marked, because "Yes." is a beat, not a readability problem. The tint is painted over the text rather than into it, so a marked-up chapter is not a modified chapter.
- **Chapter numbering that reaches every format.** Only the EPUB writer applied the layout's chapter heading; every other format printed the raw title, so a layout that said "Chapter {number}: {title}" produced one book in EPUB and a different one in Word. All seven writers resolve it now, from the chapter's position in the export rather than the folder name on disk - so excluding a chapter renumbers the ones after it. Layouts also choose the numerals (1, I, i, or One) and whether the finished heading is set in capitals, which is how one book ships as "CHAPTER SEVEN" in print and "7" in the ebook.
- **Decide what goes into the book without deleting anything.** Right-click a scene in the binder to **hold it back from exports**: it stays in the binder, keeps its words and its place in every count, and simply never reaches a compiled book. The Export view also filters by scene stage, so a draft of only the finished scenes is one tick rather than a pass through the chapter list. Archiving was the only way to keep a scene out of an export before, and archiving takes it out of the binder too - which is a different decision.
- **Print, at last.** There was no print command anywhere in Novalist, not even a hotkey. `Ctrl+Alt+P` now prints what you are looking at: the open scene in the editor, the whole book in Manuscript mode, the exposé in the Exposé, and any other view - Timeline, Plot Grid, Calendar, Relationships - as it reads, with the chrome dropped. Prose prints without the working apparatus: no toolbar, no grammar underlines, no readability tint. Your printer's dialog owns paper size, margins and print-to-PDF, which is why Novalist does not ask about any of them.
- **See what an export will contain before you run it.** The Export view now reports the chapters, scenes and words the current choices would produce, and how long the book runs in the chosen layout - recomputed as you change the selection, the layout or the stage filter. It runs the same compile the export runs, so scenes you have held back are counted exactly as they will be. The page count says "about" for every layout except Normseiten, which reports standard pages exactly, because that grid fixes the columns and the lines.
- **Snapshots restore the scene, not only its words.** A snapshot now carries the synopsis, notes, point of view, stage, label, story date, plotlines and tags as they stood, and restoring puts them back - so returning to last week's version no longer leaves you with this week's synopsis describing a different draft. Snapshots taken before this hold only prose, and restoring one of those leaves the other fields alone rather than blanking them.
- **A snapshot manager for the whole book.** The snapshots dialog has a second scope listing every snapshot in the project with the scene it belongs to, where they can be renamed - "sent to the agent" is findable a year later in a way a date is not - or deleted. Two buttons prune: keep the newest five per scene, or delete everything older than 90 days. Both also clear the snapshot folders left behind by scenes you deleted, which nothing could reach and which the manual previously told you to delete by hand with the project closed.
- **Git can be read, not only written.** Committing from inside Novalist worked; reading any of it back did not, so the one long-history path the app ships could not answer "what changed last Tuesday" without a terminal. The Git view now lists the last 30 commits with the files each one touched and a diff for any of them, lists your branches and switches between them, creates a branch and moves to it in one step, and offers to create the repository itself when the project is not in one yet.
- **Pictures can go in the prose.** A manuscript could hold no images at all - a map, a letter, a diagram, a photograph of the thing you are describing had nowhere to be. Right-click in the editor, choose **Insert image**, and Novalist copies the file into the book, asks what it shows, and places it on a line of its own. What you type becomes the alt text and travels into every export; it is asked for at insert time because asking later means never. The scene stores a path relative to the book rather than an address on your disk, so the project still opens after you move it. EPUB packages and manifests each file once, DOCX embeds it with the description attached, PDF draws it scaled to the measure and never blown up past its own size, and Markdown and LaTeX reference it. An image whose file has gone is left out instead of exported as a broken reference.
- **Chapter openers.** A chapter can carry a **subtitle** under its title - a place and a date, or where a story first appeared - and can be printed with **no heading at all**, which is what a prologue that opens straight into prose actually needs; previously the only way to get one was a chapter with a blank title. Export layouts add a **drop cap** on the chapter's first letter and a run of **small capitals** after it, honoured in EPUB, Word and LaTeX. The page still breaks before a hidden heading; only the words are gone.
- **One set of tags for the whole project.** Scenes, Codex entries and research notes each kept their own tag list, so the same word in two places was not the same tag - it could not be counted, coloured, renamed or merged. Settings has a **Tags** section now: every tag with a colour you choose and how many scenes, entries and notes carry it. Renaming reaches all three at once, renaming onto a tag that already exists merges the two, and removing takes it off everything. Codex entries can carry tags at all for the first time.
- **Scene templates.** A new scene was always blank - no point of view, no stage, no plotlines, no shape to the prose - so anyone who writes a repeatable kind of scene retyped it every time. Right-click a scene that already reads the way you want and choose **Save as scene template**; the New Scene dialog then offers **Start from**, and the new scene is born carrying the template's synopsis, prose, point of view, stage, label, tags and plotlines. The scene's own title is not captured, because a template named after one scene would put that name on every scene made from it. Deleting a template never touches the scenes made from it.
- **Saved lists can ask who is in a scene, and narrow the Manuscript view.** Two new rule fields match the cast you recorded on a scene and the entry the scene is about - so "every scene Mira is in" now finds the ones where she is present but never named, which no search of the prose can. Both match on entity id, so renaming a character does not quietly stop the list matching. The Manuscript view's toolbar also gained a saved-list drop-down: pick one and the prose, the corkboard, the outliner and the board all narrow to its scenes.
- **The Plot Grid can cross scenes with the Codex.** Its rows were plotlines and nothing else, so "which scenes is she in" had no place to be asked or answered. A drop-down now chooses characters, locations, items or lore as the rows, and ticking a cell records that the entry is in that scene - the same cast the Wiki reads for appearances, saved lists match on, and the Timeline shows. Ticking across a row is the fastest way there is to say who is in which scene, and it never depends on the name appearing in the prose.
- **A Series view, for projects with more than one book.** Every analytical read in Novalist went through the book you had open, so a World Bible character in a trilogy showed one book's appearances and a writer planning a series had nowhere to see the series. The new view - **Series**, in the Plan group of the activity bar - lists every book with its chapters, scenes and words, and gives each shared entry a cell per book, filled where it appears. A gap in the middle of a row is a thread that was dropped, which is very hard to see any other way. Appearance means the cast you recorded or a mention you confirmed; nothing is guessed from names. Reading it opens each book in turn and puts you back in the one you were in.
- **Images can be described, and the export says when they are not.** A picture had a display name and nothing else, so a reader who cannot see it got a file name read aloud. Codex images now carry **what this image shows** alongside their name, and the Export view reports how many pictures in the export have no description before you run it. Exported EPUBs declare their accessibility - access modes, features, hazards and a plain-language summary - built from what the file actually contains rather than asserted, because claiming a description that is not there is worse than claiming nothing.
- **Research plays and reads its own files.** An imported PDF got a path and a file size; audio and video had no type at all, so a recorded interview could be stored and never heard. PDFs now render in the Research panel, audio and video get players, and importing picks the type from the extension - anything the app cannot play stays a plain file rather than showing controls that do nothing. Reading a reference in another application is how a train of thought gets lost, which is what Open External is still there for.
- **Codex sheets can be arranged.** The built-in field sets were fixed and always shown, so a project that never records eye colour carried the field on every character for ever. **Arrange fields** now hides the ones you do not use and puts the rest in the order you fill them in, per entry type. Hiding keeps whatever is already written in the field - it is out of the way, not gone, and showing it again brings the contents back. The name cannot be hidden, and a field added to Novalist later appears at the end of a sheet you arranged before it existed rather than being invisible.
- **Codex sections can hold more than paragraphs.** The section toolbar was nine text marks, so a magic-system cost table, a house roster or ship stats became bulleted lists. There are buttons for a **table**, a **code block** and a **callout** now. Tables and code render properly in the Wiki, and a callout - `> [!note]`, the convention Obsidian uses - shows as a tinted aside whose colour follows its kind, while staying a plain block quote anywhere that does not know the syntax.
- **The relationship graph holds more than people, and its ties have kinds.** It graphed characters only, so "who holds this city" and "who has the sword" - the same question about a different kind of node - had nowhere to be asked. **Show** in the toolbar now adds locations, items and lore, and clicking any node opens that entry's own article. Relationships also carry a **kind of tie** - family, ally, rival, member of, owner of, place - and the graph colours the line by it. Novalist used to guess "family" from keywords in the role text, which only worked in English; an unstated kind is now left unstated and drawn neutrally rather than guessed at.
- **Search understands what you are asking.** Quick Open was one case-insensitive substring pass with no ranking, so it could not express "in the title", "not this word", or "these words in this order" - the three things anyone hunting a half-remembered line needs. Queries now take `title:`, `text:`, `notes:`, `tag:` and `kind:` scopes, `-` to exclude a word, and quotes for an exact phrase, with every term having to hold. Results are ranked: a title match beats a match in the prose, an exact title beats a partial one, matching every term beats matching one. Anything that is not syntax is searched for as written, so a stray colon looks for itself rather than failing.
- **Named milestones.** Rotating backups answer "what did this look like an hour ago", never "what did the first draft look like" - by then it has been rotated out. Name a version in Settings, Backups and press **Keep this version**, and that archive is exempt from retention entirely: not counted against the limit, never rotated out, and taken even when automatic backups are switched off. The name lives in the archive's file name, so it is still readable if you copy the ZIP elsewhere. Every archive row also gained a delete button, which is the only way to remove a milestone.
- **Compare two drafts.** Cloning a draft before a rewrite was always one click; seeing what the rewrite changed was not possible at all. The compare button next to the draft selector puts two drafts side by side, scene by scene, marking each as unchanged, changed, only-in-the-later-draft or only-in-the-earlier-one, and shows a line diff of whichever scene you pick. Scenes are matched by identity rather than title, so one you renamed mid-rewrite is still the same scene. When the right-hand side is the draft you are in, a changed scene offers to take the other version across - snapshotting what it replaces first, so it is undoable.
- **Deleting a chapter is recoverable.** It was the one structural action with no way back: a confirmed chapter delete took its scenes with it and left a backup as the only recovery. A deleted chapter now moves to the trash at the bottom of the binder with its scenes archived beside it, and **Restore** brings the whole thing back - every scene, in the order it had - at the end of the manuscript. **Delete forever** is now the only thing in the binder that destroys anything, and it asks first.
- **Your own fields now reach plotlines, timeline events and research items.** Typed fields stopped at scenes and chapters, so a plot thread, a dated event and a source each had a fixed set and anything else about them had to go in the description. Settings, Your Own Fields takes three more scopes; a plotline's fields open from its right-click menu in the Plot Grid, an event's from the event editor, and a research item's under the item itself. The same key in two scopes stays two separate questions.
- **Arrange corkboard cards freely.** The corkboard could only lay cards out in reading order grouped by chapter - the one arrangement the binder already shows. **Arrange freely** in the Manuscript toolbar lets you drag cards anywhere, with scenes from every chapter on one board, and saves where you put them with the book. A scene you have never placed takes the slot it would have had in reading order, so turning the mode on shows the book as it stands rather than a pile in the corner. **Back to reading order** forgets the arrangement. Rearranging cards never reorders the book.
- **Chart your own rating axes.** The Dashboard could chart tension, which is Novalist's number, and nothing of yours. Any scene field you defined as a Number can now be charted the same way - stakes, pace, how much the viewpoint character knows - with a picker when you have more than one. The chart scales to the largest value you actually used, so an axis running 1 to 5 and one running 0 to 100 both fill the height, and a scene you left blank stays a hairline rather than becoming a zero.
- **Lay a PDF out for a printer.** The PDF was always US Letter with a one-inch margin all round - a manuscript page, which is wrong for anything going to a press. A layout can now carry a real print page: a trim size (US Trade, Digest, Mass market, A5, Royal, Crown quarto, or your own), separate inside and outside margins that swap on facing pages, a gutter sized from the page count the way print-on-demand services specify, bleed, and paragraphs moved whole rather than leaving a line stranded at a page edge. With bleed set, the file also records where the printer should cut. Novalist does not claim PDF/X conformance - that needs a colour profile for the specific press - and the manual says so.
- **Suggest edits instead of making them.** The pen button in the editor toolbar turns on suggestion mode: typing proposes an addition rather than writing one, and deleting marks words as a cut rather than removing them. Every suggestion in a scene is listed in the inspector's Notes tab with who proposed it and buttons to take it or turn it down, one at a time or all at once, and the Inbox lists every scene in the book with edits waiting. A suggested addition counts as part of the book until somebody turns it down - it is in the word count, in a search and in an export - while a suggested cut is not, and the marks themselves never reach an exported file. Text you struck through yourself stays formatting rather than being read as a suggested cut.
- **Extensions can do considerably more.** The SDK gained research items (read and write, plus file import), comments with an author, suggested edits, scene metadata (point of view, intensity, emotion, conflict, stage, tags, plot threads, story dates, narrative mode, act and your own fields), acts, plot threads, timeline events, structural editing (rename, move, set act, trash a chapter, archive a scene), writing sections onto any Codex entry, a command bus other extensions and scripts can call, and a post-export check hook. An extension still cannot silently rewrite prose - it proposes an edit and the writer answers - and nothing in the SDK erases anything.

### Changed

- **Corkboard and Outliner open large books quickly.** They built every card and every row of the whole book before showing you anything - thousands of text boxes on a fifty-chapter manuscript, almost none of them on screen. A chapter's cards and rows are now built when you scroll near them, with their space reserved in advance so the scrollbar stays the right length and scrolling lands where you expect. Card colours are also looked up once instead of scanned per card, which was quadratic on books with hundreds of scenes.
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
- **Family relationships were upside down.** Novalist read a relationship row as what the entry is to the target rather than what the target is to the entry, so on a character whose row said "mother -> Amy" it concluded the character was Amy's mother. Every family was inverted: the kinship labels on the Relationships graph called a mother a daughter and a grandchild a grandparent. Rows read the right way round now.

- **An extension's changes show up without clicking away and back.** When an extension wrote to the Codex or changed the shape of the project, it changed the files and nothing on screen: the entry kept showing its old values and the binder kept its old order until you navigated somewhere else and returned. The interface now reloads when an extension writes.

- **A new plot thread no longer comes out the same blue as every other one.** The manual has promised an automatically assigned colour the whole time; every thread in fact got the same one, which made a grid of coloured cells say nothing about which thread was which. Each new thread now takes the next colour in a palette.

- **Two edits inside the same moment no longer lose one of their saved versions.** A Codex entry's earlier versions are named by the time they were taken, and two saves in the same millisecond — which a paste over a whole field set will do — produced the same name, so the second quietly replaced the first.

- **Relationship rows save once, when you stop typing.** Every field wrote the whole row the moment it lost focus, so crossing a row wrote it three times — and because each write also authors the other end of every tie it names, the three raced. The one that lost was reliably the last, which is the one carrying the inverse role, so the relationship appeared on one entry and not on the other. Leaving the entry writes anything still settling rather than dropping it.

- **The Relationships graph no longer snaps back when you change the reach.** Centring on somebody and then widening the hops put two requests in flight, and the older answer could arrive last and win — so the graph returned to the narrower view it was already leaving. Late answers are now discarded.

- **Relationships typed on a place, an item or a piece of lore are saved.** Novalist wrote the other half of a relationship whatever the two ends were — a sword owned by a character appearing as "owns" on the character — but only characters ever reached that code. Saving a tie on any other kind of entry went looking for a character with that entry's id, found none, and failed silently, so the row was lost the moment it was typed and the reciprocal was never written.

- **A relationship row no longer loses what you just typed.** Filling in the role and moving to the target saved the row as it stood a moment earlier, and the reply from that save landed on top of the target field while it was being typed. The target ended up empty on every entry type, characters included. Fields are now left alone while you are in them.

- **The formatting bar no longer covers the menu you just opened.** Right-clicking a word selects it so the menu can offer spellings for it, and that selection brought the floating format bar up over the top of the menu itself. The bar now waits until the menu closes.

- **The Overview column headings stand over their own columns again.** Each row of the outliner sized its own columns, so the heading row — with words like "Zusammenfassung" in it — laid itself out wider than every row beneath, and the labels drifted away from the values they named. Past a certain window width the last three columns were pushed off the side entirely. The whole table now shares one set of columns and fits without scrolling sideways.

- **The Relationships toolbar no longer runs off the edge of the window.** Search, groups, roles, five kind toggles and the "centre on" picker sat on one unbreakable row, so on a narrower window the tail of it — including the zoom readout and the scene toggle — was simply not reachable. The row wraps now. The same toolbar is used by Timeline, Calendar and the Plot Grid, which gain the same behaviour.

- **The Inspector's "Refers to" picker fits inside the Inspector.** The picker is as wide as the longest scene title in the project, and nothing stopped it from pushing itself and the add button past the panel's edge.

- **The same project no longer appears twice in the recently opened list.** Windows spells one folder several ways — `d:/git/book`, `D:\git\book`, and the same with a trailing slash — and Novalist compared those spellings literally, so opening a project the other way added a second card for it rather than moving the one you had to the front. Removing a card had the same blind spot, and could leave the duplicate behind. The list now recognises a folder whatever way it was written.

- **A save no longer goes missing when two land at once.** Novalist saves on a timer while you keep working, so a settings or scene save could arrive while the same file was already being written. On Windows the second one failed outright and its changes were silently lost. Saves of the same file now queue behind each other, and a file another program is holding for a moment is waited out rather than given up on.

- **Dimming the paragraphs you are not writing did nothing when page view was on.** Page view moves every paragraph inside a page container, and the dimming — along with paragraph styles and read-aloud's "start from the caret" — was still looking at the editor's own children, which in page view are pages rather than paragraphs. All three now find the paragraph wherever it is sitting.
- **Read-aloud uses every voice your system has, not the handful the browser exposes.** On Windows there are two voice stores; Novalist read the one the browser reads, while everything a writer installs to get more voices — a language pack, a third-party natural-voice adapter — registers in the other. A machine offering every other desktop application three hundred voices offered Novalist three, and no setting could change it. Novalist now asks the system engine directly: on the machine this was found on, three voices became three hundred and twenty-two, including the German ones that had been installed and were invisible. The voices for your writing language are listed first, because a list of three hundred in no order is one nobody reads to the end of.
- **Settings section titles were half shouting.** The eight original sections were stored in capitals and every page added since used sentence case, so the category list read as though two people had built it. All of them are sentence case now.
- Restoring an archived scene put it in the first chapter of the book, wherever it had actually come from, and always at the end. It now goes back exactly where it was — same chapter, same slot between the scenes on either side — and the archive list says which chapter each scene left. Choosing a different chapter in **Restore into** still works; that is now a deliberate move rather than the only behaviour.
- **Export formats from an extension can export part of the book.** The chapter list was hidden for them and every run produced the whole manuscript, so sending somebody the first three chapters was only possible in a built-in format. The list is shown now and the selection reaches the format.
- **Word counts are right for Chinese, Japanese, Korean and Thai.** Novalist counted runs of letters separated by spaces, so a five-hundred-character Chinese scene came out as a handful of words — and the word count, the daily goal, every target and the reading time were all wrong for a language it ships an interface for. Chinese, Japanese and Korean now count one character per word, the convention their publishers use; Thai is divided by an average word length, which is an approximation and far closer than one. Reading time follows the same split, so a Chinese chapter no longer reads as five times longer than it takes.
- **A relationship you write on a place, an item or a lore entry is now written back on the other end too.** Only characters ever authored the reciprocal, so a sword's "owned by Alice" existed on the sword and nowhere on Alice — the relationship was real from one side and invisible from the other, including to the Relationships graph. Setting an inverse role now writes it whatever the two ends are.
- **Export formats added by an extension no longer claim your book is in English.** They were told the output path and the title and nothing else, so an HTML, FictionBook or OpenDocument export of a German novel declared itself English — which is what a browser hyphenates by, a screen reader pronounces by, and a word processor spell-checks against. Every contributed format now gets the same writing language, author and cover the built-in formats already used.
- **Your book cover reaches the formats that can hold one.** No extension format could ever include it, because none of them were told where it was. HTML now embeds the cover in the file itself, so it survives being moved or mailed, and FictionBook carries it as a real cover page. The **Include the book cover** toggle appears for any format that can use it instead of only for EPUB and PDF.
- **Codex sections keep their formatting on a published website.** Bold, italics, headings, lists, quotes and links in an entry's sections were printed as the raw characters you typed — `**stubborn**` arrived as four literal asterisks — because the page generator stripped tags and stopped there. They are now rendered the way the Wiki renders them, and `[[links]]` between published entries become real links.
- **A published website is in your language.** Its own wording — Contents, Previous, Next, the group headings, the "nothing written here yet" notes — was always English, and every page declared itself English no matter what you write in.
- An extension's wizard collected your answers and then did nothing with them. A wizard reached from the command palette handed its answers back to the app rather than to the extension that defined it, so filling one in appeared to work and changed nothing. Publishing a website was the visible case.
- Extensions asking for a folder or a file had to ask you to type the path. They can open a real dialog now, so publishing a website starts with a folder picker.
- Restoring an archived scene put it in the first chapter no matter which chapter it came from or where you wanted it. The archive now has a **Restore into** picker, and the scene goes where you say.
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
