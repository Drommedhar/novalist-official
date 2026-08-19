# Narration

The Narration view shows your book as it will be read aloud — the prose exactly as you wrote it, with every spoken line marked in its speaker's colour and every stretch carrying a note about how it should be said. Then it reads it to you.

It exists because a scene tells you different things when you hear it. Dialogue that looked fine on the page turns out to be four people who all talk the same way; a paragraph that read as tense turns out to be a list. Reading your work aloud catches that, and is the one revision pass nobody does often enough because it means doing it out loud.

What makes this more than read-aloud is that the reading is **cast**. Every character can be given their own voice, the prose between the quote marks stays with the narrator, and each line carries a direction — angry, peaceful, sorrowful — worked out from what you already wrote. All of it is shown before a word is spoken, and any of it can be corrected on the line itself.

Open **Narration** from the **Write** mode, under **Hear it**, or press `Ctrl+Alt+R`. It is also in the **Go** menu with the other views.

## The view at a glance

Two panes:

- The **cast** down the left: the narrator, then every character with lines in the book, most talkative first, with their line count. Each row has a colour swatch and a voice picker.
- The **book** on the right: your chapters and scenes as continuous prose, in reading order, in your own editor font and leading.

Under it sits the transport — play, speed, and a button that jumps to the scene you have open in the editor.

The whole book is on one strip, so **scrolling is how you move around**. There is no scene to pick: the reading is not scene-sized, and a chapter you can only hear one scene at a time is not a reading of the chapter.

## Reading the page

Your prose appears as written — paragraphs, emphasis, chapter titles, scene titles. Marked on top of it:

- **A spoken line** is tinted in its speaker's colour, with a bar down its left edge. The colours are the swatches in the cast list, so the rail is the legend for the page: you can see who is talking without reading a word.
- **A line nobody could be traced to** is hatched instead of tinted. Those are the ones worth fixing, and they are findable by scrolling rather than by opening every line in turn.
- **Everything else is narration** — including the dialogue tag — and is left plain, because it is the narrator's.
- **Hovering any of it** says who reads it and how: *Mira · Angry*.

Nothing here is editable. This is where the book is listened to; the [Editor](05-editor.md) is where it is written.

## Casting the voices

The voice pickers offer the voices your operating system has installed — the same list read-aloud uses. If the list is empty, install voices through your operating system's speech settings; Windows and macOS both ship a voice manager.

- The **narrator** belongs to the book, not to a person. There is one, and it reads all the prose. A narrator that changed with the point of view would be a different book every chapter.
- A **character** you cast is read in their own voice everywhere they speak, in every chapter.
- A character you leave **uncast** is read by the narrator, and shown as uncast in the list. A half-assembled cast gives you a complete reading with some of it in the wrong voice — which you can hear and fix — rather than a reading with holes in it.

The heading above the list says how far you have got: *4 of 11 given a voice*.

The cast is stored with the book, in `.novalist/narration/cast.json`, so it travels with the project folder through Git and to another machine.

A cast naming a voice the machine you are on does not have is not broken — it is a cast you assembled somewhere else. Those rows show as having no voice, because the voice they name is not in the list to show; the reading still happens, in a voice in your writing language, until you pick one that exists here.

## Voices designed for your characters

The voices on your machine are whatever the operating system happens to ship, and none of them was made for anybody in your book. An **extension** can supply a speech engine that designs one.

Novalist itself loads no model and reaches no network. What it does is assemble the description and hold onto the result; the engine is the extension's, and the manual page for whichever engine you install says what it needs.

When an engine is installed, the cast list gains a **Prepare** button (getting an engine ready is a once-per-machine step, and it says what it will cost before it starts), and then every character's row gains **Design a voice**.

### The brief

Designing opens the **brief** — the description the voice is made from — and you can read and edit it before anything is sent. It is assembled from your Codex entry: age, gender, build, height, distinguishing features, any custom property that names the voice, and any section titled something like *Voice*, *Speech* or *Accent*. Under it are a few lines the character actually speaks, because how somebody talks describes their voice better than any adjective.

**The brief describes the instrument, never the mood.** Age, accent, pace, the register they speak in when nothing is wrong — the things that do not change. How a line is *felt* is decided per line, every time, against that one fixed identity, which is what lets a character be furious in chapter three and grieving in chapter twenty and still be recognisably the same person.

That is why emotion words are stripped out of the brief on the way through, including anything you type into it yourself. An emotion written into a design prompt is baked into the timbre, where no amount of per-line direction can get it back out — and you would have a character who sounds the same at the funeral and the wedding.

### Consent

A Codex entry set to **never** reach an AI model is honoured here. A model running on your own machine is still a model, so designing a voice for such an entry is refused, said plainly, and offered as **Design anyway** — a decision you take deliberately, per entry, or not at all. A single *section* you marked as hidden from AI stays hidden even when you allow the entry.

### The narrator

The narrator has no Codex entry, because a narrator is not a character. Their row offers **Design a voice** too, and their brief comes from the book instead: the **narrative person** and **tense** you declared, and the **logline** from the premise. What kind of book it is and who is telling it are what decide how it should be narrated.

Nothing is taken from the premise paragraph, which is where the drama lives — the same rule as everywhere else here: the brief is the instrument.

### Listen before you keep it

Designing gives you a voice to hear, not a voice you are stuck with. The dialog plays what it made and offers **Keep this voice** or **Try again**.

That is not politeness — voice design is not deterministic, and the same description asked for twice gives two different voices. Sometimes one of them is not what you asked for at all. Nothing is stored or cast until you press Keep, so a miss costs you one more press rather than becoming your character's voice until you notice.

Write the brief in the language you write in; it is understood. If you keep getting a voice that is not the one you meant, say more about it — age, build, pace, register — and try again.

### What is stored

A designed voice is stored as **audio**, in `.novalist/narration/voices/`, alongside the brief it came from. The audio is the voice: voice design is not deterministic, so re-deriving it from the description would hand you a slightly different actor every session. The brief is kept only so that designing again starts from what you asked for last time.

Designing again means a **new** actor rather than a refinement of the old one — same reason. **Delete this voice** forgets it and un-casts anybody reading in it, rather than leaving the cast pointing at something that no longer exists.

## How the reading is worked out

Novalist reads the prose itself. There is no AI involved and nothing is sent anywhere — the whole pass runs offline over your scene files.

Every quoted passage is found first, in all the quote styles Novalist supports (`"…"`, `“…”`, `„…“`, `«…»`, `»…«`, `‹…›`, `‚…'`), and the speaker of each is worked out exactly as the [Dialogue](33-dialogue.md) view works it out — from an `@`-mention you placed, a speech verb beside a character's name, an unambiguous pronoun, and so on. Each spoken line carries the same **confidence** label you see there (**Set by you**, **Named**, **Inferred**, **Nearby**, **Guessed**, **Unknown**), so a guess never reads as a fact.

Everything **between** the quoted passages is narration, and the narrator reads it. That includes the dialogue tag:

> **"Get out,"** *she said, not turning round.*

The quoted half is the character. The tag is the narrator. Reading the tag in the character's voice is the most obvious way a performed reading gives itself away as a machine, so Novalist splits them.

A line nobody could be traced to is still read — by the narrator — rather than skipped, and counted at the bottom of the cast list. A reading with silent gaps in it sounds exactly like the feature is broken.

## How a line's direction is worked out

Every segment has a **direction**: which of the sixteen emotions it should be read with, and where that came from. Four sources, tried in order, and none of them needs a model:

| Rank | Source | Shown as |
| --- | --- | --- |
| 1 | **You set it.** A direction you picked on this line wins outright and survives every re-scan. | **Set by you** |
| 2 | **The speech verb in the tag.** The prose usually says how a line was said — *she snapped*, *er flüsterte*, *他低声说道*. That is a statement about delivery you already made. | **From "snapped"** |
| 3 | **The scene's own emotion.** The [Inspector's](22-context-sidebar.md) emotion field for the scene, scaled by its intensity — your own summary of what this scene is, and the right baseline for a line that says nothing more specific. | **From the scene** |
| 4 | **Nothing.** Read plainly, rather than a guess dressed up as direction. | **Read plainly** |

The emotions are the same sixteen the Inspector's scene analysis uses, and they come from your project's writing language, so a scene marked *tense* and a line tagged *snapped* speak the same vocabulary. A writing language that ships no analysis lexicon offers no direction picker rather than an English one.

Narration is never directed by a dialogue tag's verb — *she snapped* directs the line it introduces, not the introducing. Prose takes its direction from the scene alone, and takes less of it: the paragraph describing a drowning should not be read flat, and it should not be acted either.

## Correcting it while you listen

**Click any line in the prose** and its controls open under the page: where it is, what it says, who reads it, and how.

- **The wrong speaker.** Pick anybody from the dropdown, or take one of the suggestions — where the prose did not name the speaker, the line offers its likely speakers with a percentage each (*Might be: Mira 71% · Tomas 29%*). This writes to the same place the Dialogue view writes to, so a correction made while listening is a correction made there, and the reverse. One store, two views.
- **The wrong reading.** Pick an emotion for this line, narration or dialogue. Your choice is marked **Set by you**; **Undo** hands the line back to the prose. Choosing the plain reading deliberately is itself a decision and is kept as one — it will not quietly fall back to the scene's emotion next time you open the view.

Directions are stored as metadata alongside the scene, never written into the prose. The manuscript is what a reader reads; how it is performed is a note about it.

## Hearing one line while you write it

The question in the middle of writing a line is whether it sounds right in the mouth of the person saying it, and going to another view to find out answers it too late to be any use. Select the line in the editor, right-click, and **Scene → Hear this line** speaks it where it stands.

It is looked up in the scene rather than spoken as raw text, so it arrives in the voice of whoever says it and directed the way the reading would have it — a preview in the narrator's voice of a line a character speaks would be answering a different question. Selecting a phrase is enough; the line it sits in is what gets spoken.

It needs a speech engine installed and a voice cast for that speaker, and says so rather than reading it flat when either is missing.

## Directing by hand

Sixteen names cover most lines. For the ones they do not, **By hand** opens the eight dimensions a speech engine actually takes — *happy, angry, sad, afraid, disgusted, melancholic, surprised, calm* — each from 0 to 1, blendable. *0.8 happy and 0.3 surprised* is delighted surprise, and no single word in the list is.

- **The sliders open on what will be performed**, standing register included, rather than on zero. What the screen says and what you will hear are the same thing.
- **What you set by hand is what is used.** It is not scaled by the scene's intensity and not reduced because the line is narration. You pushed the numbers; they are the numbers.
- **Everything at once is a request for nothing.** An engine takes at most 1.5 across all eight dimensions. Ask for more and it is scaled down to fit, keeping its proportions — the panel says so rather than moving your sliders behind you.

**Apply to** directs a run of lines in one go: this line and the ones after it, as far into the scene as you say. A whole argument, a whole eulogy. Thirty lines set one at a time is thirty chances to set one of them differently by accident, and the reason to direct a run by hand is that it is one performance. A run stops at the scene break; directing across one is not what anybody means by "this argument".

**Like that line** is the input of last resort and the most precise one there is. Some deliveries have no name in any vocabulary, and once you have heard the one you wanted you can point at it instead of describing it: pick any line rendered in this sitting and the engine performs the new one in the manner of that clip. The voice stays the designed one — only the delivery is borrowed. It reaches engines that accept an emotion reference; the others fall back to the numbers, so setting one never leaves a line worse off.

### A standing register

The slider button beside a name on the cast rail sets that character's **standing register**: dimensions added to every line they speak. For somebody who is always more clipped, or warmer, or wearier than the prose bothers to say each time — a note to the actor about the part, rather than a direction on any one line.

It runs below zero as well as above, because a character flatter than the prose says needs emotion taken away rather than added. It is added to the line's own direction rather than replacing it, so a furious line from a habitually flat character is still furious. The narrator has one too.

Registers live in the cast sheet, so they belong to the book rather than to any scene.

## Reading it

With an engine installed and voices designed, **Play** performs the book in those voices, with each line directed as the panel says. With no engine, it reads with the voices your operating system has — the same transport, the same highlight, the same corrections; only the voices differ.

A performed reading is rendered a stretch at a time and played as each stretch arrives, so pressing Play does not wait for the chapter and pressing Stop does not throw away a chapter's worth of work. The audio goes to a cache beside the application, never into your project, and is deleted when you stop: a repository should not grow by tens of megabytes because somebody pressed Play, and a rendering of your manuscript should not outlive the sitting it was made in.

**Play** starts at the top of the book, or — if you have a line selected — at that line, which is what **Play from here** means. The segment being spoken is highlighted and the page scrolls to keep it in view. **Stop** ends the reading; there is no pause, because the speech engine speaks a passage whole and there is nothing to resume from.

**Speed** runs from 0.5 to 2, the same scale as read-aloud.

**Go to the scene I am writing** jumps the page to whichever scene is open in the editor — the way back to where you were, once the whole book is one strip.

Play is unavailable until something is cast: with no narrator and no character voices there is nothing to read with, and the transport says so rather than sitting silent. Leaving the view stops the reading, since the engine would otherwise keep speaking with no control left on screen to stop it.

The highlight is painted over the view and never touches the document, so listening to a chapter does not mark it as edited.

## Where this data lives

Nothing is duplicated. The prose and the reading are built from your scene files every time you open the view, so they always reflect what is actually written. Two things are stored:

- The **cast**, including each character's standing register — `.novalist/narration/cast.json`, one file per book.
- The **speakers and directions you set by hand** — alongside the rest of the scene's metadata in `.novalist/scenes.json`, the same file the Dialogue view's corrections live in.
- Any **designed voices** — `.novalist/narration/voices/`, as audio plus the brief each came from.

Everything Novalist does here it does offline. Working out who says what and how it should be read involves no model at all, and speech comes either from the voices your operating system already has or from an engine you installed, which is not allowed to reach the network. It works with the cable out.

## Making an audiobook

A reading you can listen to in the app is not a file you can send anybody. **Audiobook** in the [Export view](20-export.md) renders the book to audio, chapter by chapter, and packages it.

It is compiled from exactly the same selection every other format is: the chapters you ticked, the front and back matter, and the compile-time replacements. A book whose ebook says one thing and whose audiobook says another would be worse than having no audiobook at all.

### Before it starts

The panel says what it is about to cost: how many chapters, how many words, how long the finished reading will be, and how long this machine will take. That last figure comes from what this machine did on its last renders — not from a benchmark, not from ours. Between a laptop on its processor and a desktop with a graphics card the difference is two orders of magnitude, so until you have finished one render it says **Unknown until the first render** rather than inventing a number.

### While it runs

Rendering a novel takes hours, and you are expected to go on writing through it. Progress shows in the status bar wherever you are, and:

- **It is resumable.** A chapter already rendered from the same words, the same cast and the same directions is not rendered again. Edit chapter nine and only chapter nine is re-rendered — the difference between a five-minute correction and an overnight one. **Render every chapter again** overrides that when you want the whole thing fresh.
- **It is stoppable.** Stop ends it within a few lines, and every chapter that finished stays finished. Start again later and it picks up where it left off.
- **Lines it could not speak are counted, not hidden.** A character with no voice, or a voice designed on another machine, is reported at the end rather than leaving a silence you would only find by listening.

### What comes out

| Delivery | What it is |
| --- | --- |
| **One file with chapter marks (M4B)** | What an audiobook is, and what a player expects: one file, chapter marks you can skip through, your cover and your metadata. |
| **One MP3 per chapter** | The plain alternative. It plays everywhere, including in cars and on players that never learned M4B. |
| **One WAV per chapter, unencoded** | The rendered audio exactly as it came out, with a chapter list beside it. |

M4B and MP3 need **ffmpeg** on your machine. Novalist does not ship it and does not fetch it — it is several hundred megabytes under a licence that would change what Novalist itself is distributed as. Without it the chapters are still delivered, as WAV files with a chapter list, and the panel says why. The hours of rendering are never thrown away because a tool is missing.

## Where to go next

- [Export](20-export.md) — every other edition of the book, compiled from the same selection the audiobook is.
- [Dialogue](33-dialogue.md) — the same speaker attribution and the same overrides, as a list per character rather than as the book: every line one person speaks, in story order.
- [Extensions](24-extensions.md) — installing a speech engine, and what an engine is allowed to do.
- [Accessibility](39-accessibility.md#read-aloud) — read-aloud in the editor, which is the one-voice version of this.
- [Manuscript view](10-manuscript.md) — the same whole-book strip, for reading rather than listening.
- [Codex (Characters, Locations, Items, Lore)](06-codex.md) — character names, aliases and the Gender field, which are what attribution matches your prose against.
- [Inspector](22-context-sidebar.md) — the scene's emotion and intensity fields, which every undirected line in the scene is read with.
