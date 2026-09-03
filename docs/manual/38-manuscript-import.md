# Importing a manuscript

If your book already exists somewhere else, you do not have to retype it or paste it in scene by scene. Choose **File → Import a manuscript...** in the menu bar.

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
| Scrivener | `.scriv`, `.scrivx` | The draft as parts, chapters and scenes, plus Codex entries and research. See below. |

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

A Scrivener project keeps its binder in a `.scrivx` manifest inside a project folder, which Scrivener normally names with a `.scriv` suffix. Use the same **Choose a manuscript** button as every other format. On Windows, Linux and the direct-download Mac build, open the project folder or package and select its `.scrivx` file. In the sandboxed Mac App Store build and the iOS app, you may instead select the `.scriv` package or project folder so Novalist receives access to the documents beside the binder. If you select a `.scrivx` there, confirm its exact parent project folder when asked. A folder with more than one manifest is never guessed: enter it and select the intended `.scrivx`. The folder itself can have any name or no `.scriv` suffix, and an explicitly selected manifest is always authoritative. Both Scrivener 2 and Scrivener 3 projects are read, including projects created by Scrivenix or moved onto a case-sensitive Linux filesystem.

The Mac App Store build temporarily copies the selected binder and its `Files` folder into Novalist's private app container because its importer runs in a separate process from the file picker. Large projects can therefore need comparable free space while the import dialog is open. The copy is removed when you choose another source, close the dialog, complete the import or quit Novalist; stale copies left by a crash are cleared on the next start. Settings, icons and snapshots are not copied.

Novalist takes the binder at its word rather than guessing from headings — the binder already says where the chapters are. It reads what each part of the binder *is* from Scrivener's own markers rather than from folder titles, so a project in German, or one where you renamed the draft folder, imports the same as any other.

### Where each folder goes

Scrivener marks only three things: the draft folder, the trash, and its template sheets. Everything else in the binder is your own arrangement, and no set of rules reads that reliably. A project whose draft folder is empty because you were about to start a fresh draft, with nine finished drafts filed under a folder called "Old", is an ordinary way to work — and read by rules alone it is a project with no manuscript and a great deal of research.

So the rules produce a starting point and you correct it. Choosing a Scrivener manifest shows **Where each folder goes**: one row per binder folder, already filled in with what Novalist made of it, and a menu on each row to say otherwise.

| Destination | What the folder becomes |
| --- | --- |
| **Manuscript** | Chapters and scenes of the book you are importing into. |
| **New draft** | A [draft](03-projects-and-books.md) of that book, named after the folder. Its own folders become that draft's chapters. |
| **New book** | A new book in the project, named after the folder. |
| **Codex: characters** | [Codex](06-codex.md) entries, as characters. |
| **Codex: places** | Codex entries, as places. |
| **Research** | [Research](15-research.md) items, with the folder title as a tag. |
| **Do not import** | Left in Scrivener, and named in "Not brought across" before you commit. |

The rows are the **top level of the binder and the level below it**. That is enough to separate nine drafts filed inside one folder — "Old" is one row and each draft inside it is a row of its own — and stopping there keeps the part, chapter and scene shape *inside* a draft the binder's business rather than a wall of menus.

**Setting a folder sets everything inside it.** Point "Old" at **New draft** and each of the nine drafts inside it becomes a draft of its own, in one action rather than nine. Every row inside can still be changed afterwards, so putting the "Old Notes" folder back to **Research** leaves the nine drafts alone. The folder is a starting point for what is inside it, not a decision about it.

Changing a row re-reads the project and updates the plan underneath, so the counts, the chapter list and the drafts and books that would be created always describe the choices currently on screen. Nothing is written until you press **Import**.

**What you say wins.** A folder of character sketches sent to Research arrives as research notes, icons and all — otherwise the choice would be a suggestion. Left alone, those same sketches still become Codex entries.

A folder sent to **Manuscript** keeps its own name as the act above its chapters, so merging an old draft into a book that already has chapters does not scatter them among the ones already there.

If the rules already read your project correctly, change nothing and press **Import**: it behaves exactly as it always has.

### The draft

Left alone, only the **Draft** folder becomes manuscript, whatever it is called in your project.

- A folder holding other folders is a **part**, and becomes an act.
- A folder holding documents is a **chapter**.
- The documents inside it become **scenes**, in binder order.
- Anything nested deeper than a chapter is flattened into it, so nesting is lost rather than text.
- A document sitting directly in the draft with no folder around it lands in a chapter called "Imported".

A draft that never got chapter folders — just a run of documents, which is how a draft often looks before it has been organised — comes across as one chapter called "Imported" holding those documents as its scenes. This applies to each draft or book separately, so several such drafts imported together each keep their own.

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

Everything else in the binder that carried content becomes a **research item** unless you sent it somewhere else: notes keep their prose, and PDFs, pictures, recordings, video and other imported files are **copied into your project** so it stays portable and the Scrivener project can be deleted afterwards. The folder each item sat in comes across as a tag.

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

When an import would create drafts or books, each is listed by name with what it would hold, so nine drafts are nine lines rather than one total to divide by nine.

Nothing touches your project until you press **Import**. If the split is wrong — the wrong heading level, a manuscript with no structure at all — close the dialog and nothing has happened.

## What import does to your project

Chapters are **added to the end of the book**. Nothing already in the project is changed or replaced.

Drafts and books an import creates are added beside what you already have, and the import leaves you on the book and draft you started from — importing nine old drafts does not move you off the one you were about to write.

That means running an import twice gives you the book twice rather than destroying anything. If that happens, delete the duplicate chapters — recoverable, which replacing would not have been.

## If nothing is found

"Nothing could be read from that file" means the format is not supported, the file is empty, or it is damaged. Novalist never reports an error for a file it cannot read: you did not write that file, so "nothing found" is the useful answer. With diagnostic logging enabled, the log records why an unreadable Scrivener project stopped — for example, a missing or ambiguous manifest, invalid XML, missing binder, or unreadable document. Parser failures include a safe explanation, error code and, for XML, the line and position. The explanation describes the parser problem without copying names or values from the source; duplicate failures from the preview's two parsing passes are collapsed into one entry.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — what the import creates.
- [Projects & Books](03-projects-and-books.md) — importing into a specific book and draft.
- [Codex](06-codex.md) — where imported character and place sketches land.
- [Research](15-research.md) — where imported notes, PDFs and media land.
- [Export](20-export.md) — the other direction.
