# Exposé

The Exposé view is where you write the pitch document that goes to an agent or a publisher alongside your manuscript: the synopsis that gives away the ending, the character sketches, the market positioning. It is a full editing surface with one thing the ordinary editor does not have — a live count of how much of your allowance you have used.

Each book in a project has its own exposé.

## Opening the Exposé

In the activity bar, click **Exposé** in the **Publish** group.

## Writing

The writing surface is the same editor you use for scenes: the same font, theme, and spell checking. Your text saves automatically two seconds after you stop typing, and again when you leave the view.

An exposé is a **line-oriented** document, not prose, and the export treats it that way:

- Each paragraph is one line of the exported page, wrapped at 60 characters when it is longer.
- Two paragraphs that follow each other stay on **adjacent** lines. Nothing is inserted between them. This is what keeps a block like "Genre: ... / Schauplatz: ... / Erzählweise: ..." reading as a tight list.
- An **empty paragraph** is the only thing that opens a blank line. Press Enter twice where you want air.

This is the opposite of how a manuscript scene exports, where every paragraph is followed by a blank line. The exposé rule is what makes the exported page reproduce your document line for line.

### Headings

Three buttons in the bar above the editor set the style of the paragraph the caret is in. Select several paragraphs first to restyle them all at once. The active button always shows the style of the paragraph you are in, and styled paragraphs are drawn larger and bolder so you can see them at a glance.

- **Title** — the document title. Exports upper-cased, with no blank line forced around it.
- **Section** — a section heading. Exports upper-cased, with a blank line before and after.
- **Body** — ordinary text. Clears any style back off the paragraph.

These map onto the two heading levels of a Markdown document, so an exposé written elsewhere as Markdown and pasted in keeps its structure.

## The counters

The bar above the editor shows two live numbers, updated a moment after you stop typing:

- **Characters** — every character of your text, spaces included. This is the count publishers mean when they ask for "maximum 15,000 characters".
- **Normseiten** — how many German standard pages your text fills once it is laid out at 60 characters per line and 30 lines per page. This is the count they mean when they ask for "no more than 10 pages".

The two are counted independently. A text can be inside its character budget and over its page budget, because blank lines between paragraphs and short heading lines consume grid lines without consuming many characters.

## Setting a limit

Type a number into **Character limit** or **Page limit** and click away. The limit is stored with the book, so it is there the next time you open the project.

- Leave a field empty for no limit. The counter still shows you where you are, it just never changes colour.
- As you approach the limit (from 90% on), the counter turns amber.
- Once you pass it, the counter turns red.

Limits never block typing. Overshooting and then cutting is how the work gets done — the counter is there to tell you how much you have to cut, not to stop you mid-sentence.

## Exporting

Click **Export Normseiten**. Pick a location in the file dialog and Novalist writes a DOCX laid out as German standard pages:

- Courier New 12pt, exactly 20pt line spacing.
- A4, with 3.0 cm top, 4.5 cm bottom, 2.5 cm left, and 3.2 cm right margins.
- Every line hard-wrapped at 60 characters, and a page break forced every 30 lines — so the page count in the document is exactly the page count you were shown while writing.
- A running header carrying the title and "Seite *x* von *y*".

The header carries the book's name. The **body contains only your own text** — nothing is prepended to it, so the title line at the top of the page is the one you wrote. If the exposé is still empty, nothing is written and the view tells you so.

The same layout is available for your manuscript — see the **Normseiten** preset in [Export](20-export.md).

## Where it lives on disk

The exposé is stored as `Expose.novalist` at the root of the book's folder, next to `Drafts/` and `Characters/`. It contains HTML and opens in any text editor. The two limits live in `.novalist/project.json` with the rest of the book's metadata.

## Where to go next

- [Export](20-export.md) — the Normseiten preset and the other output formats.
- [Editor](05-editor.md) — the writing surface, its formatting, and its paragraph styles.
- [Projects & Books](03-projects-and-books.md) — the multi-book model and the folder layout.
