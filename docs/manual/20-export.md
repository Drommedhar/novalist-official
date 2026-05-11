# Export

The Export view turns your book into a file you can send to a beta reader, an editor, or a publisher. Built-in formats include EPUB, DOCX, PDF, Markdown, Final Draft (Fountain), LaTeX, and Codex Markdown. Extensions can add more.

## Opening Export

Click the **upload** icon in the activity bar, or use the export hotkey.

## The export form

- **Format** — drop-down of every available format:
  - **EPUB** — e-book.
  - **DOCX** — Microsoft Word.
  - **PDF** — print-ready PDF.
  - **Markdown** — single-file markdown.
  - **Final Draft** — screenplay format (Fountain-compatible).
  - **LaTeX** — `.tex` source.
  - **Codex Markdown** — markdown export including codex entries as appendix.
  - Plus formats contributed by extensions.
- **Title** — the title of the exported document. Defaults to the book's name.
- **Author** — author name. Defaults to the project's author setting (Settings → Appearance / Project).
- **Include title page** — toggle.
- **Preset** — optional. Pre-configured export settings; built-in presets include:
  - **Default** — sensible all-rounder.
  - **Shunn** — Shunn Modern Manuscript Format (for submissions).
  - Any extension-contributed presets.
- **Chapter selection** — checkboxes for every chapter. Defaults to all. Use **Select all** / **Deselect all** for quick toggling.

After filling in, click **Export**. A file picker asks where to save. Status messages appear while the job runs; a toast confirms when it's done.

## Built-in format details

### EPUB

- Includes chapter breaks, scene breaks, paragraph styles.
- Embeds the book cover image, if set.
- Compatible with major e-readers (Kindle via conversion, Apple Books, Kobo, etc.).

### DOCX

- Standard Word document with paragraph styles mapped to Word styles:
  - Heading 1 — Chapter title.
  - Heading 2 — Scene title (if rendered).
  - Body Text — paragraph.
  - Quote — Blockquote.
- Comments are dropped by default (toggleable in extension presets).
- Footnotes preserved as Word footnotes.

### PDF

- Print-ready. The page size and margins come from Settings → Editor → Book page format. Cover image embedded if set.

### Markdown

- Single `.md` file. Chapter headings at H1, scenes at H2 (if titled). Comments and footnotes preserved as markdown footnotes.

### Final Draft (Fountain)

- Plain-text screenplay markup. Useful for projects that double as scripts.

### LaTeX

- `.tex` source you can compile with `pdflatex` or `xelatex`. Includes a basic preamble suitable for novels.

### Codex Markdown

- Markdown export with an appendix containing your codex entries (characters, locations, items, lore). Useful for delivering a "world bible" version of the project to a collaborator.

## Presets

A preset is a saved combination of format + options. The bundled presets:

- **Default** — format-appropriate defaults.
- **Shunn** — submission-format DOCX (12pt Courier-equivalent, double-spaced, first-line indents, page breaks at chapter starts).

Extensions can contribute presets. Pick a preset to load its settings; you can still override individual fields before clicking Export.

## Selecting chapters

Use cases for partial exports:

- A single chapter to send to a critique partner.
- The first three chapters as a submission packet.
- Only chapters at Final status, to publish a polished excerpt.

Combine chapter selection with the chapter-status filter you may already have applied in the Manuscript view, but note that Export's chapter list shows all chapters regardless of status — you pick what you want via the checkboxes.

## Tips

- **Always export EPUB and DOCX before sharing.** EPUB for reading, DOCX for tracked-changes editing.
- **Use Shunn for submissions.** Even if a publisher has slightly different requirements, Shunn is closer to standard than a casual export.
- **Embed a cover image.** EPUB readers display the cover prominently; a missing cover signals "amateur".
- **Re-export every release.** Re-run the export for any new beta reader instead of sending an old file — it's the only way to be sure they see the latest revisions.

## Where to go next

- [Settings](23-settings.md) — book page format and font settings affect PDF and the book preview.
- [Extensions](24-extensions.md) — add new export formats via the SDK.
