# Export

The Export view turns your book into a file you can send to a beta reader, an editor, or a publisher. Seven formats are built in: EPUB, DOCX, PDF, Markdown, Final Draft, LaTeX, and Codex Markdown.

## Opening Export

In the binder's view rail (below the chapter tree), click **Export** in the **Publish** group.

## The export form

- **Format** — drop-down of the available formats:
  - **EPUB (e-book)**
  - **DOCX (Word)**
  - **PDF**
  - **Markdown**
  - **Final Draft (.fdx)**
  - **LaTeX**
  - **Codex (Markdown)** — manuscript plus your codex entries as an appendix.

  Any formats contributed by installed [extensions](24-extensions.md) appear below the built-in ones in the same drop-down.
- **Preset** — a layout preset that sets fonts, spacing, and margins. Built-in presets are:
  - **Default** — Georgia 12pt, 1.5 line spacing; a readable PDF/EPUB.
  - **Shunn Manuscript Format** — the industry-standard submission format: Courier 12pt, double-spaced, with the Shunn header.
  - **Ebook Flow** — tighter spacing for digital reading: Georgia 11pt, 1.4 line spacing, narrower margins.

  A short description of the selected preset is shown beneath the drop-down. Extensions can contribute additional presets.
- **Shunn Manuscript Format** — for **DOCX** and **PDF** only, a toggle that switches the export straight to the Shunn submission preset (and disables the Preset drop-down while it is on).
- **Title** — the title of the exported document. Defaults to the project's name.
- **Author** — author name printed on the title page and in document metadata.
- **Title page** — toggle. Include or omit a generated title page.
- **Chapter selection** — a checkbox per chapter, with **Select All** and **Select None** buttons and a running "*n* of *m*" count. All chapters are included by default; untick chapters to exclude them. This list is hidden for the **Codex** format and for extension-contributed formats, which always cover the whole project.

Click **Export**. The system file save dialog asks where to save; pick a location and filename (the extension is pre-filled to match the format). The button shows "Exporting…" while the job runs and a result line reports success or failure.

## Built-in format details

### EPUB

- Includes chapter breaks, scene breaks, paragraph styles.
- Embeds the book's cover image, if one is set.
- Compatible with major e-readers (Kindle via conversion, Apple Books, Kobo, etc.).

### DOCX

- Standard Word document with paragraph styles mapped to Word styles:
  - Heading 1 — chapter title.
  - Heading 2 — scene title (if rendered).
  - Body Text — paragraph.
  - Quote — blockquote.
- Comments are dropped.
- Footnotes preserved as Word footnotes.

### PDF

- Print-ready PDF with the book's cover image embedded if set.

### Markdown

- Single `.md` file. Chapter headings at H1, scenes at H2 (if titled). Footnotes preserved as markdown footnotes.

### Final Draft

- `.fdx` screenplay format. Useful for projects that double as scripts.

### LaTeX

- `.tex` source you can compile with `pdflatex` or `xelatex`. Includes a basic preamble suitable for novels.

### Codex Markdown

- Markdown export with an appendix containing your codex entries (characters, locations, items, lore). Useful for delivering a "world bible" version of the project to a collaborator.

## Selecting chapters

Use cases for partial exports:

- A single chapter to send to a critique partner.
- The first three chapters as a submission packet.
- Only chapters at Final status, to publish a polished excerpt.

For chapter-based formats the list shows all chapters regardless of status — you pick what you want via the checkboxes, or use **Select All** / **Select None**. The Codex and extension formats export the whole project and hide the list.

## Tips

- **Export EPUB and DOCX before sharing.** EPUB for reading, DOCX for tracked-changes editing.
- **Embed a cover image.** EPUB readers display the cover prominently; a missing cover signals "amateur".
- **Re-export every release.** Re-run the export for any new beta reader instead of sending an old file — it's the only way to be sure they see the latest revisions.

## Where to go next

- [Settings](23-settings.md) — editor and appearance settings.
- [Manuscript view](10-manuscript.md) — read the whole book before you export it.
