# Export

The Export view turns your book into a file you can send to a beta reader, an editor, or a publisher. Eight formats are built in: EPUB, DOCX, PDF, Markdown, Final Draft, LaTeX, Codex Markdown, and Codex PDF.

## Opening Export

In the activity bar, click **Export** in the **Publish** group.

## The export form

- **Format** — drop-down of the available formats:
  - **EPUB (e-book)**
  - **DOCX (Word)**
  - **PDF**
  - **Markdown**
  - **Final Draft (.fdx)**
  - **LaTeX**
  - **Codex (Markdown)** — your codex entries as a Markdown world bible, with images written to a folder beside the file.
  - **Codex (PDF)** — the same world bible as a single PDF with the images drawn into the document.

  Any formats contributed by installed [extensions](24-extensions.md) appear below the built-in ones in the same drop-down.
- **Preset** — a layout preset that sets fonts, spacing, and margins. Built-in presets are:
  - **Default** — Georgia 12pt, 1.5 line spacing; a readable PDF/EPUB.
  - **Shunn Manuscript Format** — the industry-standard submission format: Courier 12pt, double-spaced, with the Shunn header.
  - **Ebook Flow** — tighter spacing for digital reading: Georgia 11pt, 1.4 line spacing, narrower margins.
  - **Normseiten** — German standard pages: Courier New 12pt, 60 characters per line, 30 lines per page. **DOCX only** — the other formats ignore it.

  A short description of the selected preset is shown beneath the drop-down. Extensions can contribute additional presets.
- **Shunn Manuscript Format** — for **DOCX** and **PDF** only, a toggle that switches the export straight to the Shunn submission preset (and disables the Preset drop-down while it is on).
- **Title** — the title of the exported document. Defaults to the project's name.
- **Author** — author name printed on the title page and in document metadata.
- **Title page** — toggle. Include or omit a generated title page.
- **Include the book cover** — toggle, shown for **EPUB** and **PDF** only, since no other format has anywhere to put a cover. On by default. The image is the book's cover, falling back to the project cover — the same one the Dashboard and the welcome screen show. Turn it off for a submission manuscript, which should not carry one.
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
- Footnotes preserved as Word footnotes.

#### Normseiten (DOCX)

Selecting the **Normseiten** preset switches DOCX to the German standard-page layout, which German agents and publishers ask for by name. Rather than letting Word reflow the text, Novalist lays it out on a fixed grid so the page count in the document is exact and countable:

- Courier New 12pt with exactly 20pt line spacing.
- A4, with 3.0 cm top, 4.5 cm bottom, 2.5 cm left, and 3.2 cm right margins.
- Every line hard-wrapped at 60 characters — words are never split, so an over-long word takes a line of its own.
- A page break forced every 30 lines.
- A running header with the title and "Seite *x* von *y*".

Because pagination comes from the grid, chapters do not start on a fresh page: chapter titles appear as upper-case headings in the flow, with a blank line above and below, and scenes are separated by a centred `* * *`. Bold and italic are dropped — a Normseite is monospace plain text by definition.

To write and export a pitch document in the same layout, see [Exposé](32-expose.md).

### PDF

- Print-ready PDF. When a cover is set and **Include the book cover** is on, the cover becomes a full page ahead of the title page, scaled to fit the trim size and centred so it is never stretched.
- A cover the PDF engine cannot decode is skipped rather than failing the export.

### Markdown

- Single `.md` file. Chapter headings at H1, scenes at H2 (if titled). Footnotes preserved as markdown footnotes.

### Final Draft

- `.fdx` screenplay format. Useful for projects that double as scripts.

### LaTeX

- `.tex` source you can compile with `pdflatex` or `xelatex`. Includes a basic preamble suitable for novels.

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

## Where to go next

- [Exposé](32-expose.md) — write the pitch document with live Normseiten counts, and export it in the same layout.
- [Settings](23-settings.md) — editor and appearance settings.
- [Manuscript view](10-manuscript.md) — read the whole book before you export it.
