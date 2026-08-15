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
| Rich text | `.rtf` | Paragraphs, headings, bold and italic, lists, and alignment. See [Formatting](#formatting). |
| Scrivener | `.scriv` | The draft as parts, chapters and scenes, plus Codex entries and research. See below. |

Import recovers **words and the shape they were written in** — not the page they were written on. Fonts, type sizes, colours, margins and compile settings stay in the program you left; images, footnotes and comments are not carried over either.

## Formatting

Rich text and Scrivener carry more than paragraphs. What comes across is the formatting that means something — the emphasis you would lose the sense of a sentence without:

- **Bold**, *italic*, underline and strikethrough, kept where they sit inside a sentence rather than applied to the whole paragraph.
- Superscript and subscript.
- Headings, block quotations and verse, from the named styles the source used.
- Numbered and bulleted lists, as lists rather than as typed-out numbers.
- Centred, right-aligned and justified paragraphs.
- Scene-break ornaments, kept as scene breaks.

Everything else — the typeface, the point size, the line spacing, the indents — is left behind on purpose. Novalist styles your prose from your own settings, so importing a book does not import somebody else's page design along with it.

Curly quotes, apostrophes, ellipses, en and em dashes arrive as themselves. A file written on a different platform, or in a language whose characters sit outside the Latin alphabet, reads correctly rather than arriving speckled with stray characters.

A document Novalist cannot make sense of is reported on its own and the rest of the import goes ahead, so one damaged file no longer costs you the whole book.

## Scrivener projects

A Scrivener project is a folder rather than a file, so it has its own button: **Choose a Scrivener project**. Point it at the `.scriv` folder. Both Scrivener 2 and Scrivener 3 projects are read.

Novalist takes the binder at its word rather than guessing from headings — the binder already says where the chapters are. It reads what each part of the binder *is* from Scrivener's own markers rather than from folder titles, so a project in German, or one where you renamed the draft folder, imports the same as any other.

### The draft

Only the **Draft** folder becomes manuscript, whatever it is called in your project.

- A folder holding other folders is a **part**, and becomes an act.
- A folder holding documents is a **chapter**.
- The documents inside it become **scenes**, in binder order.
- Anything nested deeper than a chapter is flattened into it, so nesting is lost rather than text.
- A document sitting directly in the draft with no folder around it lands in a chapter called "Imported".

Two chapters with the same name stay two chapters. Scrivener's own novel template names every part "Part" and every chapter "Chapter", and they are kept apart by identity rather than by title.

**Empty documents still come across.** Outlining in empty binder documents is how most Scrivener projects start, and that outline is the part worth keeping.

A document you never named becomes **Scene 1**, **Scene 2** and so on in binder order, rather than an untitled row you have to identify by opening it.

Scrivener's **named styles** are read from the project itself, so a paragraph you styled as a heading, a block quotation or verse arrives as that, and the markers Scrivener writes into its own files to record them never show up in your prose.

### Scene metadata

| In Scrivener | In Novalist |
| --- | --- |
| Synopsis card | Scene synopsis |
| Document notes | Scene notes |
| Status ("First Draft", "Done", your own) | Scene stage, created if the book has no stage by that name |
| Label ("Red", your own) | Scene label, created if the book has no label by that name |
| Include in Compile, unticked | Scene excluded from export |
| Custom metadata fields | Scene properties — a list field becomes a set of allowed values, anything else becomes text |

### The Codex

Character and setting sketches become **Codex entries** — characters and places respectively. The sketch prose lands in a **Sketch** section and any document notes in a **Notes** section, because a filled-in Scrivener sheet is already a set of headed answers.

Sketches filed into sub-folders still come across; the grouping folders are flattened.

Scrivener's blank **template sheets** are deliberately not imported. They would arrive as a character called "Character Sketch" whose every field is a prompt.

### Research

Everything else in the binder that carried content becomes a **research item**: notes keep their prose, and PDFs, pictures, recordings, video and other imported files are **copied into your project** so it stays portable and the Scrivener project can be deleted afterwards. The folder each item sat in comes across as a tag.

A research note keeps its formatting as Markdown, so headings, emphasis and lists in a set of notes survive as headings, emphasis and lists rather than flattening into one block of text.

Front matter — a title page, a copyright page, a dedication — is research too. Novalist builds its own front matter at export time, so the words are kept and the arrangement is not.

### What does not come across

The preview names what will be left behind before you commit, so you find out here rather than by noticing something missing later. The **Trash** and the **Template Sheets** folder stay in Scrivener, as do collections, keywords, label colours, compile settings and snapshot history.

Your Scrivener project is never modified. If the import is not what you wanted, delete the imported chapters and the original is untouched.

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

Choosing a file **reads it without writing anything**. You get the format Novalist recognised, how many chapters, scenes and words it found, and the chapter list with per-chapter counts. For a Scrivener project it also names the acts the chapters will land in, and counts the characters, places and research items that will be created.

Nothing touches your project until you press **Import**. If the split is wrong — the wrong heading level, a manuscript with no structure at all — close the dialog and nothing has happened.

## What import does to your project

Chapters are **added to the end of the book**. Nothing already in the project is changed or replaced.

That means running an import twice gives you the book twice rather than destroying anything. If that happens, delete the duplicate chapters — recoverable, which replacing would not have been.

## If nothing is found

"Nothing could be read from that file" means the format is not supported, the file is empty, or it is damaged. Novalist never reports an error for a file it cannot read: you did not write that file, so "nothing found" is the useful answer.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — what the import creates.
- [Projects & Books](03-projects-and-books.md) — importing into a specific book and draft.
- [Codex](06-codex.md) — where imported character and place sketches land.
- [Research](15-research.md) — where imported notes, PDFs and media land.
- [Export](20-export.md) — the other direction.
