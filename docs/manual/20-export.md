# Export

The Export view turns your book into a file you can send to a beta reader, an editor, or a publisher. Eight formats are built in: EPUB, DOCX, PDF, Markdown, Final Draft, LaTeX, Codex Markdown, and Codex PDF.

## Opening Export

In the activity bar, click **Export** in the **Publish** group.

## The export form

- **What to export** — **The manuscript** or **The codex (world bible)**. What comes out and what kind of file it comes out as are two different questions, so they are two drop-downs. The codex export writes your codex entries as a world bible, with images written to a folder beside a Markdown file or drawn into the document for a PDF.
- **Format** — the file the export produces. Which formats are listed depends on what you are exporting:
  - The manuscript: **EPUB (e-book)**, **DOCX (Word)**, **PDF**, **Markdown**, **Final Draft (.fdx)**, **LaTeX**, plus any formats contributed by installed [extensions](24-extensions.md).
  - The codex: **Markdown** or **PDF**.
- **Preset** — the layout that sets fonts, spacing, and margins. The drop-down lists the built-in layouts, marked `(built-in)`, followed by every layout you have authored yourself (see [Export layouts](#export-layouts) below) — one list, so anything you can make is something you can pick. Built-in layouts are:
  - **Default** — Georgia 12pt, 1.5 line spacing; a readable PDF/EPUB.
  - **Shunn Manuscript Format** — the industry-standard submission format: Courier 12pt, double-spaced, with the Shunn header.
  - **Ebook Flow** — tighter spacing for digital reading: Georgia 11pt, 1.4 line spacing, narrower margins.
  - **Normseiten** — German standard pages: Courier New 12pt, 60 characters per line, 30 lines per page. **DOCX only** — the other formats ignore it.

  A short description of the selected layout is shown beneath the drop-down when it has one. Extensions can contribute additional layouts.
- **Title** — the title of the exported document. Defaults to the project's name.
- **Author** — author name printed on the title page and in document metadata.
- **Title page** — toggle. Include or omit a generated title page.
- **Include the book cover** — toggle, shown for **EPUB**, **PDF** and any extension format that can hold a cover. On by default. The image is the book's cover, falling back to the project cover — the same one the Dashboard and the welcome screen show. Turn it off for a submission manuscript, which should not carry one.
- **Chapter selection** — a checkbox per chapter, with **Select All** and **Select None** buttons and a running "*n* of *m*" count. All chapters are included by default; untick chapters to exclude them. This list is hidden for the **Codex** formats and for extension-contributed formats.
- **Codex entry selection** — for the two **Codex** formats the chapter list is replaced by a checkbox per codex entry, grouped by Characters, Locations, Items, and Lore and sorted by name inside each group, exactly as the exported file is ordered. Everything is ticked by default; untick the entries you do not want in the file — for example, to send a collaborator only the characters they are writing.
  - A **search box** above the list filters entries by name as you type.
  - **Select All** and **Select None** apply to the entries currently shown, so you can search for a group of entries and tick or untick them in one click without disturbing the rest of your selection.
  - The "*n* of *m*" count below the list always reports your whole selection, not just the filtered part.

Click **Export**. The system file save dialog asks where to save; pick a location and filename (the extension is pre-filled to match the format). The button shows "Exporting…" while the job runs and a result line reports success or failure.

## Front and back matter

Open **Front and back matter** at the bottom of the Export view to add the pages around your story: half title, title page, copyright, dedication, epigraph, table of contents, foreword, preface, prologue, epilogue, afterword, acknowledgments, about the author, also by this author, or a custom page of your own.

These are **typed**, not chapters. That matters because each kind is set differently in the exported book, and faking them as chapters gives every one of them chapter treatment — a chapter heading over your dedication, a chapter number on your copyright page.

Each page has:

- **Heading** — leave it empty to use the convention for that kind. Kinds that traditionally carry no heading at all (half title, title page, copyright, dedication, epigraph) print none; the rest print their name. Type your own heading to override either way.
- **Content** — the page's text.
- **Include in exports** — off keeps the page but leaves it out of the file, for something written but not ready.
- **List in the table of contents** — pre-set per kind. A foreword or prologue is listed; a copyright page is not.
- **Put this after the story** — moves a page between front and back matter. Epilogue, afterword, acknowledgments, about the author and also-by start at the back; everything else starts at the front.

Use the arrows to order pages within the front or back group.

A page with no content is skipped even when included — an empty page is not a book page yet.

In **EPUB**, each page becomes its own file with the correct `epub:type` (`dedication`, `copyright-page`, `epigraph`, and so on), so readers and stylesheets can set a copyright page differently from an epigraph without guessing. In **DOCX**, each starts on a new page.

## Sending a manuscript to an editor and getting it back

Export **DOCX**, send it to whoever is reading for you, and read their marked-up copy back in with **Import editor's changes...** at the bottom of the Export view.

**What goes out.** Unresolved scene comments travel with the DOCX as real Word comments, anchored to the phrase they were attached to, so your editor sees them in Word's review pane and can reply to them. Resolved comments are left behind — they record a finished conversation, not a note the editor needs. A comment whose anchor text no longer appears in the prose is still exported; it just arrives without a highlight rather than being dropped.

**What comes back.** Choosing the returned `.docx` shows two lists:

- **Comments** — with the author, what they wrote, and the passage they marked. **Add comments to the open scene** attaches them to whichever scene you have open, with the editor's name kept at the front of each note.
- **Tracked changes** — every insertion and deletion, with who made it. These are shown for you to work through, **not applied automatically**. Novalist did not lay out the file your editor edited, so silently rewriting your prose from it would risk damage that is hard to notice and harder to undo. Read the list, make the edits you agree with.

A file that is not a Word document, or has no review marks in it, reports "nothing found" rather than an error.

## Built-in format details

### EPUB

- Includes chapter breaks, scene breaks, paragraph styles.
- Embeds the book's cover image when one is set and **Include the book cover** is on. The cover becomes the first page of the book, ahead of the title page, and is registered both the EPUB 3 way (`properties="cover-image"`) and the EPUB 2 way (`meta name="cover"`) so retailers and Kindle both find it. JPEG, PNG, GIF and WebP are accepted; anything else is skipped.
- Records the book's language from your writing language (Settings → Writing assistance), so a German or Chinese book is not shelved as English at the retailer. Typographic variants like "German (low quotes)" resolve to the plain language code.
- A missing or unreadable cover file never fails the export — the book is produced without one.
- Compatible with major e-readers (Kindle via conversion, Apple Books, Kobo, etc.).

### DOCX

- Standard Word document with paragraph styles mapped to Word styles:
  - Heading 1 — chapter title.
  - Heading 2 — scene title (if rendered).
  - Body Text — paragraph.
  - Quote — blockquote.
- Comments are dropped.
- Footnotes become **real Word footnotes** — they sit at the foot of the page the anchor lands on, and Word renumbers them if you add or delete one.

#### Normseiten (DOCX)

Selecting the **Normseiten** preset switches DOCX to the German standard-page layout, which German agents and publishers ask for by name. Rather than letting Word reflow the text, Novalist lays it out on a fixed grid so the page count in the document is exact and countable:

- Courier New 12pt with exactly 20pt line spacing.
- A4, with 3.0 cm top, 4.5 cm bottom, 2.5 cm left, and 3.2 cm right margins.
- Every line hard-wrapped at 60 characters — words are never split, so an over-long word takes a line of its own.
- A page break forced every 30 lines.
- A running header with the title and "Seite *x* von *y*".

Because pagination comes from the grid, chapters do not start on a fresh page: chapter titles appear as upper-case headings in the flow, with a blank line above and below, and scenes are separated by a centred `* * *`. Bold and italic are dropped — a Normseite is monospace plain text by definition.

To write and export a pitch document in the same layout, see [Exposé](32-expose.md).

### EPUB

- EPUB 3. Footnotes become **popup notes**: the anchor is a `noteref` link and the note itself an `aside` at the end of the chapter file, which is what lets a reader show it in place instead of jumping away.

### PDF

- Print-ready PDF. When a cover is set and **Include the book cover** is on, the cover becomes a full page ahead of the title page, scaled to fit the trim size and centred so it is never stretched.
- Footnotes are set as **endnotes at the end of their chapter**, numbered from one per chapter, with a matching `[n]` marker in the prose. PDF is the one format where they are not at the foot of the page: the layout engine sets text a line at a time and cannot reserve the bottom of a page part-way through a paragraph.
- A cover the PDF engine cannot decode is skipped rather than failing the export.
- By default the page is US Letter with a one-inch margin all round — a manuscript page, right for reading on screen or submitting. For anything going to a printer, turn on **Lay out for a printer** in the layout (see [Laying out for a printer](#laying-out-for-a-printer)).

### Markdown

- Single `.md` file. Chapter headings at H1, scenes at H2 (if titled).
- Footnotes use **Markdown footnote syntax**: a `[^n]` reference where the anchor sits, with the definitions collected at the end of the file. Numbering runs across the whole document, since two scenes both starting at one would collide.

### Final Draft

- `.fdx` screenplay format. Useful for projects that double as scripts.

### LaTeX

- `.tex` source you can compile with `pdflatex` or `xelatex`. Includes a basic preamble suitable for novels.
- Footnotes become `ootnote{...}`, so LaTeX sets and numbers them itself.

### Codex Markdown

- A `.md` world bible of your codex entries (characters, locations, items, lore) — fields, custom properties, relationships, and section text, with entries sorted by name inside each group.
- Group headings and fixed field labels (Role, Age, Type, Relationships, …) are written in your interface language. Your own custom property names are used exactly as you typed them.
- Characters whose age is set as a birth date omit the Age line, which would otherwise just repeat the date.
- Entry images are copied into a `<filename>_images` folder next to the file and linked from the Markdown. The folder is not created when none of the exported entries has an image.
- Useful when the recipient wants to edit the text, or feed it into another tool.

### Codex PDF

- The same world bible as a single, self-contained PDF: entry images are drawn into the document, so there is no sidecar folder and nothing to lose when the file is forwarded.
- Every entry starts on its own page, so a printed copy can be handed out or filed entry by entry. Images are scaled to fit within three inches.
- **PDF bookmarks** — the reader's outline pane lists Characters, Locations, Items, and Lore, with every entry nested under its group, so you can jump straight to a character in any PDF reader.
- Section text keeps its shape: line breaks, blank lines, `#` headings, `*` bullet lists, and `**bold**` spans are laid out as formatted text rather than printed as raw markers.
- Labels and the birth-date rule work the same as in the Markdown export above.
- A title page is included when the **Title page** toggle is on.
- Images Novalist cannot read are skipped rather than failing the export.
- Useful for handing a finished reference document to a collaborator, cover artist, or editor.

## Selecting chapters

Use cases for partial exports:

- A single chapter to send to a critique partner.
- The first three chapters as a submission packet.
- Only chapters at Final status, to publish a polished excerpt.

For chapter-based formats the list shows all chapters regardless of status — you pick what you want via the checkboxes, or use **Select All** / **Select None**. The Codex formats swap this list for the codex entry picker, and extension formats export the whole project and hide the list entirely.

## Tips

- **Export EPUB and DOCX before sharing.** EPUB for reading, DOCX for tracked-changes editing.
- **Embed a cover image.** EPUB readers display the cover prominently; a missing cover signals "amateur".
- **Re-export every release.** Re-run the export for any new beta reader instead of sending an old file — it's the only way to be sure they see the latest revisions.

## Export layouts

Novalist ships four layouts — Default, Shunn manuscript, Ebook flow, Normseiten — and **Export layouts** on the Export view lets you make your own.

The panel edits whichever layout is picked in the **Preset** drop-down above, so what you are editing and what you are about to export with can never drift apart. **Duplicate** switches the export to the new copy, since that is the one you meant to work on.

Built-in layouts cannot be edited, only duplicated. That is deliberate: a layout named after a submission standard that no longer matches it is worse than no layout at all, and nothing would tell you. **Duplicate** gives you a copy carrying all of the original's settings, which is a far better starting point than an empty one.

A layout controls:

| Setting | What it does |
| --- | --- |
| **Body font** and **size** | The typeface and point size of the prose. |
| **Line spacing** | As a multiplier of the font size. |
| **Margin**, **first-line indent**, **space above a chapter title** | Page geometry, in inches. |
| **Scene separator** | What is printed between scenes. Defaults to `* * *`; make it anything, including blank. |
| **Chapter heading** | Use `{number}` and `{title}`. `Chapter {number}: {title}` gives "Chapter 3: The Fall"; the default is the title alone, which is what a novel with named chapters wants. Leave `{title}` out for chapters that ship numbered and untitled. |
| **Drop cap on the first letter of each chapter** | Sets the chapter's opening letter as a drop cap. Honoured in EPUB (CSS), DOCX (Word's own framed drop cap) and LaTeX (`lettrine`). The PDF writer lays out lines rather than shapes and prints the opener plainly; Markdown has no typography to carry one. |
| **Words in small capitals after it** | How many words of the first sentence follow the drop cap in small capitals (0-12). Two or three is the convention. |
| **Chapter numerals** | What `{number}` is written in: `1, 2, 3`, `I, II, III`, `i, ii, iii`, or `One, Two, Three`. Worded numerals are English only. |
| **Set chapter headings in capitals** | Sets the finished heading in capitals, so a print layout can read "CHAPTER SEVEN" while the ebook layout of the same book reads "7". |
| **Print scene titles** | Off for a novel, where the separator is the whole break. On for a collection, or a draft going to someone who needs to name the scenes back to you. |
| **Extra ebook CSS** | Appended to the EPUB stylesheet, so your rules win over Novalist's by cascade order. The one place you can reach the look of the ebook itself rather than of the page. |
| **Lay out for a printer** | Turns on the print page below: trim size, inside and outside margins, gutter, bleed and widow control. PDF only. |

### Laying out for a printer

A manuscript is one page size with one margin all round, because it is read on a screen or in a ream on a desk. A bound book is not, and none of the difference is a matter of taste — a file that gets it wrong is rejected by the printer or comes back with text disappearing into the spine.

Tick **Lay out for a printer** in the layout editor and you get:

| Setting | What it does |
| --- | --- |
| **Trim size** | The finished page after cutting. Pick a named size — US Trade 6x9, Digest, Mass market, A5, Royal, Crown quarto, A4, US Letter — or type your own measurements. |
| **Inside** and **outside margin** | The inside margin is at the binding, the outside at the cut edge. They are different numbers because the two edges have different jobs. |
| **Top** and **bottom margin** | As they sound. |
| **Mirror margins on facing pages** | On for a bound book: the inside margin is on the left of a right-hand page and on the right of a left-hand one. Turn it off for anything read on screen or printed single-sided. |
| **Size the gutter from the page count** | Extra room at the binding, scaled to how thick the book is. A 700-page book curves far more at the spine than a 90-page one and swallows nearly a quarter-inch more. The steps are the ones print-on-demand services publish. Turn it off to set the gutter yourself. |
| **Bleed** | How far artwork runs past the trim, so a cut landing a hair inside the line does not leave a white sliver. Leave it at zero for a text-only interior; a full-page image needs it. |
| **Keep paragraphs off page edges** | Moves a paragraph whole rather than leaving one line stranded at the foot of a page or carrying one alone onto the next. |

Page one is a right-hand page, as it is in every bound book, so odd pages bind on the left.

When bleed is set, the PDF records **where the printer should cut** as well as how big the sheet is. A file that does not say is the commonest reason a print job comes back.

**What this is not:** Novalist does not claim PDF/X conformance. PDF/X requires an embedded colour profile describing the press the book is going to, which is a decision about your printer rather than about your book. If your printer asks for PDF/X specifically, run the exported file through their preflight or a PDF tool that can attach the profile they want. Everything else on this page — the trim, the margins, the gutter, the bleed and the trim marks — is in the file Novalist writes.

## Images

Pictures placed in the prose are carried into EPUB, DOCX, PDF, Markdown and LaTeX. EPUB packages and manifests each file once however many chapters use it; DOCX embeds it with the alt text as the picture's description; PDF draws it scaled to the text measure and never larger than its own size. An image whose file has gone is left out rather than exported as a broken reference.

## What this export will contain

Above the chapter list, Novalist reports what the current choices would produce: how many chapters and scenes, how many words, and how long the book runs in the chosen layout. It is recomputed as you change the selection, the layout or the stage filter, and it runs the same compile the export runs — so held-back scenes and filtered stages are counted exactly as they will be, not estimated.

If any picture in the export has no description, the line says so: a reader who cannot see it gets nothing, and an EPUB carrying one cannot honestly claim to be accessible. See [Accessibility](39-accessibility.md#describing-images).

The page count is an estimate for every layout except **Normseiten**, which reports "N standard pages" because that grid fixes the columns and the lines, so the count is arithmetic rather than a guess. Elsewhere it reads "about N pages": real pagination depends on hyphenation and widow control in whatever renders the file.

## Printing

`Ctrl+Alt+P`, or **Print** in the [command palette](25-command-palette.md), prints what you are looking at.

- In the **editor**, that is the open scene. With the editor split, it is the pane your caret is in.
- In **Manuscript mode**, it is the whole book as the view lays it out — the closest thing to a printed draft without exporting first.
- In the **Exposé**, it is the exposé.
- In any other view — Timeline, Plot Grid, Calendar, Relationships, Dashboard — it is that view, with the toolbar, activity bar, binder, inspector and status bar dropped and the content given the page.

Printing prose leaves the working apparatus out: no floating toolbar, no grammar underlines, no readability tint, no word counts between scenes. Your printer's own dialog handles paper size, margins and "print to PDF", which is why Novalist does not ask you about any of them.

`Ctrl+P` is [Quick Open](31-quick-open.md) and has been since before there was anything to print, so Print took `Ctrl+Alt+P`. Both are rebindable in **Settings → Hotkeys**.

## Choosing what goes in

Beyond the chapter checkboxes:

- **Hold back from exports** — right-click a scene in the binder. The scene stays in the binder, keeps its word count and still counts towards your goals; it simply never reaches a compiled book. The menu item reads **Include in exports again** on a scene already held back. With several scenes selected it applies to all of them.
- **Only scenes at these stages** — tick the [stages](10-manuscript.md#scene-stages) an export should include. Leave **Every stage** ticked to export everything, which is what an export with no filter has always done. This is how a draft export of only the finished scenes, or a revision pass over only the outlined ones, is produced without touching the book.

A scene held back is held back whatever the stage filter says. The filter narrows what is included; it never overrides an explicit exclusion.

The heading is resolved by every writer — EPUB, DOCX, PDF, Markdown, LaTeX, Final Draft and Normseiten — from the chapter's position in the export, not from the folder name on disk. Excluding a chapter renumbers the ones after it, because the numbers describe the book being exported.

Layouts are stored with the book, not globally — a submission format for a novel and one for a short-story collection have no reason to share a list. Values that would produce a file nobody can open (a zero font size, a margin wider than the page) fall back to something sane rather than being stored.

## Publishing metadata

**Publishing metadata** on the Export view holds what a shop, a library and a distributor need to know about the book, beyond its title and author. All of it is optional, and anything left blank is simply not written into the exported file.

| Field | What it does |
| --- | --- |
| **ISBN** | Becomes the EPUB's package identifier — the number a retailer keys the book on. Type it however it is printed; the hyphens are stripped on the way out. The panel shows you the digits that will actually be written, or tells you when what you typed is not a usable ISBN so you find out here rather than at ingestion. |
| **Publisher** | `dc:publisher`, and a line on the title page. |
| **Series** and **Position in the series** | Stated as an EPUB collection, so a trilogy shelves as a trilogy instead of three unrelated books. Position takes `2.5` for a novella between two volumes. Also printed under the title as "The Ravens, Book 2". |
| **Description** | The blurb. `dc:description`. |
| **Subjects** | Comma-separated genre words, or BISAC codes if you have them. One `dc:subject` each; shops use these to shelve the book. |
| **Rights** | Your copyright line. `dc:rights`. |
| **Publication date** | `dc:date`. ISO format (`2026-03-01`) is the safest. |

Until now an exported EPUB carried author, title, identifier and language and nothing else, so there was no ISBN for a retailer to key on and no way for a book to say it was the second of a trilogy.

## Where to go next

- [Exposé](32-expose.md) — write the pitch document with live Normseiten counts, and export it in the same layout.
- [Settings](23-settings.md) — editor and appearance settings.
- [Manuscript view](10-manuscript.md) — read the whole book before you export it.
