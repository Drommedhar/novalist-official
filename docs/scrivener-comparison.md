# Novalist vs Scrivener — Feature Comparison

A comprehensive comparison of Novalist against [Scrivener](https://www.literatureandlatte.com/scrivener/overview), the industry-standard writing software by Literature & Latte. This document identifies feature gaps and opportunities for Novalist to reach parity or surpass Scrivener.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Novalist has this feature |
| ⚠️ | Novalist has partial/limited support |
| ❌ | Novalist is missing this feature |
| 🔮 | Planned / in development |

---

## 1. Manuscript & Editor

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Rich text editing (bold, italic, etc.) | ✅ | ✅ | ✅ | Novalist uses WebView2-based editor |
| Styles system (heading, block quote, etc.) | ✅ Named styles with compile-time transforms | Basic formatting | ⚠️ | **Missing:** Named/semantic styles that can be re-mapped at compile/export time |
| Page View mode | ✅ See pages fill up as you type | ❌ | ❌ | No WYSIWYG page simulation |
| Scrivenings mode (edit multiple docs as one) | ✅ Stitch sections into continuous view | ✅ Manuscript view | ✅ | Novalist's manuscript view shows all scenes together as a continuous document |
| Split editor (up to 4 panes) | ✅ Up to 4 documents side-by-side | ❌ | ❌ | No split/multi-pane editor |
| Full-screen / distraction-free writing | ✅ Customizable composition mode | ❌ | ❌ | No dedicated full-screen/zen mode |
| Comments & annotations | ✅ Inline comments and annotations | ❌ | ❌ | No inline commenting system |
| Highlights | ✅ Highlight text for review | ❌ | ❌ | No text highlighting for editorial review |
| Lists support | ✅ | ✅ | ✅ | |
| Image & table insertion | ✅ | ⚠️ | ⚠️ | Image gallery exists but inline insertion in editor unclear |
| Spellcheck | ✅ | ✅ | ✅ | Via WebView2 context menus |
| Auto-save | ✅ | ✅ | ✅ | |
| Find & Replace | ✅ | ⚠️ | ⚠️ | Basic search exists; **Missing:** project-wide search & replace |

---

## 2. Project Organization

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Hierarchical binder (folders/docs) | ✅ Unlimited depth | ✅ Books → Chapters → Scenes | ⚠️ | Novalist has 3-level hierarchy; Scrivener allows arbitrary nesting |
| Drag-and-drop reordering | ✅ | ✅ | ✅ | |
| Multi-book projects | ❌ One manuscript per project | ✅ | ✅ | **Novalist advantage** — shared World Bible across books |
| Custom icons per document | ✅ | ❌ | ❌ | No custom icons for scenes/chapters |
| Document templates | ✅ Character sheets, location sheets, etc. | ✅ Entity templates | ✅ | |
| Research folder (mixed media) | ✅ Store images, PDFs, web pages, audio, video alongside manuscript | ❌ | ❌ | **Major gap** — No dedicated research folder for mixed media |
| Import files (Word, PDF, images, audio, video, web) | ✅ Broad format import | ⚠️ | ⚠️ | Image import exists; no Word/PDF/audio/video import |
| Collections (saved search lists) | ✅ Smart & static collections | ❌ | ❌ | No saved document collections or smart filters |
| Metadata (labels, status, keywords, custom fields) | ✅ Extensive customizable metadata | ⚠️ | ⚠️ | Scene metadata exists (POV, emotion, intensity, tags) but less flexible than Scrivener's open-ended system |

---

## 3. Outlining & Planning

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Corkboard (index cards) | ✅ With synopses, rearrangeable | ❌ | ❌ | **Major gap** — No corkboard/index card view |
| Freeform corkboard mode | ✅ Place cards anywhere | ❌ | ❌ | |
| Outliner view (columns, metadata) | ✅ Configurable columns, word counts, synopses | ❌ | ❌ | No dedicated outliner view with customizable columns |
| Synopsis per document | ✅ Summary text on each index card | ⚠️ | ⚠️ | Scene notes exist but no dedicated synopsis field |
| Label / color-coding | ✅ Color-coded labels on documents | ❌ | ❌ | No color-coded labels for scenes/chapters |
| Status tracking per document | ✅ (To Do, First Draft, Revised, etc.) | ✅ | ✅ | Scene status: Outline, First Draft, Revised, Edited, Final |

---

## 4. World Building & Character Management

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Character profiles | ⚠️ Via custom templates only | ✅ Dedicated, detailed profiles | ✅ | **Novalist advantage** — first-class character system with physical attributes, relationships, images |
| Location management | ⚠️ Via custom templates | ✅ Hierarchical locations | ✅ | **Novalist advantage** |
| Item catalog | ❌ | ✅ | ✅ | **Novalist advantage** |
| Lore / World Bible | ❌ | ✅ Categorized lore entries | ✅ | **Novalist advantage** |
| Entity relationships | ❌ | ✅ Bidirectional with roles | ✅ | **Novalist advantage** |
| Character evolution per chapter | ❌ | ✅ Per-chapter overrides | ✅ | **Novalist advantage** |
| Custom entity types | ❌ | 🔮 In development | 🔮 | |
| Mind mapping (Scapple integration) | ✅ Via companion app Scapple | ❌ | ❌ | No mind mapping or relationship visualization |

---

## 5. Timeline

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Visual timeline | ❌ No built-in timeline | ✅ | ✅ | **Novalist advantage** — Vertical/horizontal, zoomable, filterable |
| Event categories & color-coding | ❌ | ✅ | ✅ | Plot points, character events, world events |
| Timeline filtering | ❌ | ✅ | ✅ | By character, location, source |

---

## 6. Export & Compile

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Compile / export system | ✅ Extremely powerful & flexible | ✅ Basic export | ⚠️ | Scrivener's compile is its killer feature |
| EPUB export | ✅ | ✅ | ✅ | |
| DOCX export | ✅ | ✅ | ✅ | |
| PDF export | ✅ | ✅ | ✅ | |
| Markdown export | ⚠️ Via MultiMarkdown | ✅ | ✅ | |
| RTF export | ✅ | ❌ | ❌ | |
| Final Draft export | ✅ | ❌ | ❌ | Not relevant unless screenwriting is a goal |
| LaTeX / Pandoc support | ✅ | ❌ | ❌ | |
| Compile presets / formats | ✅ Multiple output presets with different formatting | ⚠️ | ⚠️ | Only SMF preset; Scrivener has dozens of compile formats |
| Per-section compile formatting | ✅ Different formatting per section type | ❌ | ❌ | No section-level compile customization |
| Front/back matter injection | ✅ | ⚠️ Title page only | ⚠️ | No back matter, copyright page, dedication, etc. |
| Style → format mapping at compile | ✅ Styles transform differently per output | ❌ | ❌ | |
| Chapter selection for export | ✅ | ✅ | ✅ | |

---

## 7. Snapshots & Version Control

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Snapshots (per-document versioning) | ✅ Snapshot before editing, compare & roll back | ❌ | ❌ | **Missing:** Document-level snapshots |
| Snapshot comparison (diff) | ✅ | ❌ | ❌ | |
| Git integration | ❌ (Only manual backups) | ✅ Built-in Git panel | ✅ | **Novalist advantage** — though less writer-friendly than snapshots |
| Auto-backup on open/close | ✅ | ❌ | ❌ | No automatic project backups |

---

## 8. Targets & Statistics

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Manuscript word count target | ✅ | ✅ | ✅ | |
| Session word count target | ✅ | ✅ Daily goal | ✅ | |
| Per-document word count target | ✅ | ❌ | ❌ | No per-scene or per-chapter targets |
| Writing history (daily log) | ✅ | ❌ | ❌ | No historical writing activity log |
| Character count targets | ✅ | ❌ | ❌ | |
| Deadline tracking | ✅ | ✅ | ✅ | |
| Dashboard / statistics overview | ⚠️ Basic | ✅ Detailed dashboard | ✅ | **Novalist advantage** — richer analytics, pacing analysis, echo phrase detection |

---

## 9. Screenwriting

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Script mode / screenplay formatting | ✅ Built-in script mode | ❌ | ❌ | No screenplay format support |
| Stage play formatting | ✅ | ❌ | ❌ | |
| Comic script formatting | ✅ | ❌ | ❌ | |

---

## 10. Cross-Platform & Sync

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Windows | ✅ | ✅ | ✅ | |
| macOS | ✅ | ⚠️ Avalonia-based (possible) | ⚠️ | Based on Avalonia UI but unclear if macOS builds are distributed |
| iOS | ✅ Dedicated app | ❌ | ❌ | No mobile app |
| Dropbox / cloud sync | ✅ | ❌ | ❌ | No built-in cloud sync (Git is the alternative) |

---

## 11. AI & Extensions

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| AI assistant | ❌ | ✅ | ✅ | **Novalist advantage** — AI chat, story analysis, multiple providers |
| Extension / plugin system | ❌ | ✅ | ✅ | **Novalist advantage** — Full SDK with hooks for UI, export, entities, themes |
| Extension store | ❌ | 🔮 In development | 🔮 | **Novalist advantage** |
| Custom themes via extensions | ❌ | ✅ | ✅ | **Novalist advantage** |

---

## 12. UI & Quality of Life

| Feature | Scrivener | Novalist | Status | Notes |
|---------|-----------|----------|--------|-------|
| Customizable toolbar | ✅ | ✅ Ribbon with extension support | ✅ | |
| Keyboard shortcuts | ✅ | ✅ Fully customizable | ✅ | |
| Dark mode | ✅ | ✅ System/Light/Dark | ✅ | |
| Multiple languages (UI) | ⚠️ Limited | ✅ Extensible locale system | ✅ | **Novalist advantage** |
| Project templates (novel, thesis, etc.) | ✅ Multiple starter templates | ❌ | ❌ | No project-level templates for different writing genres |
| Typewriter scrolling | ✅ | ❌ | ❌ | Keep current line centered while typing |

---

## Summary: Key Missing Features (Priority)

### 🔴 High Priority (Core writing experience)

| # | Feature | Impact |
|---|---------|--------|
| 1 | **Corkboard / Index Card View** | The most iconic Scrivener feature. Essential for visual planners. |
| ~~2~~ | ~~**Scrivenings Mode** (view multiple scenes as one)~~ | ~~Already implemented — Manuscript View shows all scenes together.~~ |
| 3 | **Split Editor** (side-by-side documents) | Essential for referencing research/other chapters while writing. |
| 4 | **Full-Screen / Distraction-Free Mode** | Universally expected in writing software. |
| 5 | **Snapshots** (per-document versioning + diff) | Writer-friendly version control, more intuitive than Git for non-technical users. |
| 6 | **Research Folder** (store mixed media alongside manuscript) | Major workflow feature for writers who gather reference material. |

### 🟡 Medium Priority (Power user features)

| # | Feature | Impact |
|---|---------|--------|
| 7 | **Named Styles with Compile Mapping** | Enables semantic formatting that transforms per output format. |
| 8 | **Advanced Compile Presets** | More export templates (paperback, ebook, manuscript submission, etc.) |
| 9 | **Collections / Smart Filters** | Saved search lists for tracking specific document groups. |
| 10 | **Project-Wide Search & Replace** | Find/replace across entire manuscript. |
| 11 | **Comments & Annotations** | Inline editorial markup. |
| 12 | **Writing History Log** | Track daily writing activity over time. |
| 13 | **Outliner View** (configurable columns) | Tabular overview of manuscript with metadata. |
| 14 | **Per-Scene/Chapter Word Count Targets** | Granular goal setting. |
| 15 | **Color-Coded Labels** | Visual status/category indicators on scenes/chapters. |

### 🟢 Low Priority (Nice-to-have)

| # | Feature | Impact |
|---|---------|--------|
| 16 | **Project-Level Templates** (Novel, Thesis, Short Story) | Starter scaffolding for new projects. |
| 17 | **Page View Mode** | WYSIWYG page simulation while writing. |
| 18 | **Typewriter Scrolling** | Keep cursor centered vertically. |
| 19 | **Custom Document Icons** | Visual flair in the project tree. |
| 20 | **RTF / LaTeX Export** | Niche but valued by academic users. |
| 21 | **Screenwriting Mode** | Only relevant if targeting screenwriters. |
| 22 | **iOS / Mobile App** | Large undertaking, but broadens audience. |
| 23 | **Cloud Sync** (non-Git) | Simpler sync for non-technical users. |
| 24 | **Auto-Backup on Open/Close** | Safety net beyond Git. |
| 25 | **Mind Mapping / Relationship Visualization** | Visual brainstorming tool. |

---

## Novalist Advantages Over Scrivener

Novalist already surpasses Scrivener in several areas:

1. **World Building** — First-class characters, locations, items, lore with relationships and per-chapter evolution. Scrivener only has freeform text documents.
2. **Timeline** — Built-in visual timeline with filtering. Scrivener has nothing comparable.
3. **AI Assistant** — Integrated AI for story analysis and chat. Scrivener has no AI features.
4. **Extension System** — Full SDK with hooks for every part of the UI. Scrivener is not extensible.
5. **Multi-Book Projects** — Shared world Bible across books. Scrivener is one manuscript per project.
6. **Git Integration** — Built-in version control. Scrivener relies on manual backups.
7. **Modern Analytics** — Pacing analysis, echo phrase detection, detailed dashboard. Scrivener's statistics are basic.
8. **Localization** — Extensible multi-language UI. Scrivener has limited language support.
9. **Free & Open** — No license cost (vs. Scrivener's $49–$80 price tag).

---

*Last updated: April 10, 2026*
