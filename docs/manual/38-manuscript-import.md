# Importing a manuscript

If your book already exists somewhere else, you do not have to retype it or paste it in scene by scene. Open the backstage drawer (the burger in the toolbar) and choose **Import a manuscript...**.

## Formats

The dialog lists the extensions it can read, so you know what to look for before the file picker opens.

| Format | Extension | What is recovered |
| --- | --- | --- |
| Word | `.docx` | Paragraphs and heading levels. Tracked deletions are skipped. |
| OpenDocument | `.odt` | Paragraphs and outline levels. |
| EPUB | `.epub` | Content documents in reading order, with heading levels. |
| Markdown | `.md`, `.markdown` | `#` headings, paragraphs, and `***` / `---` scene breaks. |
| Plain text | `.txt` | Paragraphs, "Chapter N" lines, and ornament scene breaks. |
| Rich text | `.rtf` | Paragraphs only. Formatting is not recovered. |

Import is **prose only**. Bold and italic, images, footnotes and comments are not carried over — the goal is to get your words into Novalist with the right structure, not to reproduce another program's layout.

## How chapters and scenes are worked out

Novalist trusts what the file actually says, and guesses only when it has to.

1. **Heading levels win.** A Word document styled with Heading 1 per chapter is unambiguous, so those become chapters and the next level down becomes scenes. This works whatever language Word is set to, because the style is stored by name rather than by its translated label.
2. **Only if the file carries no headings at all** does Novalist look at the text for chapter openings — "Chapter 12", "Kapitel 3", "第 3 章", or a line that is just a number or roman numeral.

That order matters. In a properly styled manuscript, a line of dialogue reading "Chapter two was the hardest" stays prose, because the file already told Novalist where the chapters are.

A line that ends in sentence punctuation is never treated as a heading, however it starts.

**Scene breaks** come from ornament lines — `***`, `* * *`, `---`, a lone `#` — or from a heading one level below the chapter heading.

A stretch of prose with no breaks at all is split into several scenes rather than becoming one enormous scene the editor struggles with.

Chapters and scenes with nothing to name them get numbered.

## The preview

Choosing a file **reads it without writing anything**. You get the format Novalist recognised, how many chapters, scenes and words it found, and the chapter list with per-chapter counts.

Nothing touches your project until you press **Import**. If the split is wrong — the wrong heading level, a manuscript with no structure at all — close the dialog and nothing has happened.

## What import does to your project

Chapters are **added to the end of the book**. Nothing already in the project is changed or replaced.

That means running an import twice gives you the book twice rather than destroying anything. If that happens, delete the duplicate chapters — recoverable, which replacing would not have been.

## If nothing is found

"Nothing could be read from that file" means the format is not supported, the file is empty, or it is damaged. Novalist never reports an error for a file it cannot read: you did not write that file, so "nothing found" is the useful answer.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — what the import creates.
- [Projects & Books](03-projects-and-books.md) — importing into a specific book and draft.
- [Export](20-export.md) — the other direction.
