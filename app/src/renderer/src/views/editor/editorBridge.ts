/**
 * Bridge to editor.html running in a same-origin iframe. Outbound calls invoke
 * the editor's global functions directly on its contentWindow (same contract
 * the Avalonia host used via ExecuteScript); inbound messages arrive through
 * window.parent.postMessage as { novalistEditor: jsonString }.
 */

/** The editor page's global functions the host is allowed to call. */
export interface EditorWindow extends Window {
  setContent(html: string): void
  setTheme(
    bg: string,
    fg: string,
    caret: string,
    selectionBg: string,
    pageBg: string,
    pageFg: string,
    scrollbarThumb?: string,
    scrollbarThumbHover?: string,
    scrollbarThumbActive?: string
  ): void
  setFont(family: string, size: number): void
  /** Where a book-relative image path hangs off, for display in the frame. */
  setImageBase(base: string): void
  /** Places an image on its own line after the caret's paragraph. */
  insertImageAtCaret(storedPath: string, alt: string): void
  /** Tints each sentence by how hard it is to read. */
  setReadabilityEnabled(enabled: boolean): void
  /** Graded sentences plus the band colours, as JSON. */
  setReadability(json: string): void
  /** Speaks the scene, from the caret's paragraph or from the top. */
  startReadAloud(fromCaret: boolean, rate: number, voiceUri: string | null): void
  /** The host has spoken one sentence through the system engine; advance. */
  onSentenceSpoken(ok: boolean): void
  stopReadAloud(): void
  /** Leading, letter spacing (px) and the gap between paragraphs (em). */
  setReadingComfort(lineHeight: number, letterSpacing: number, paragraphSpacing: number): void
  setLanguage(lang: string): void
  setSpellCheck(enabled: boolean): void
  /** Mobile mode: full-width text (no margin comment gutter) + touch-sized UI. */
  setMobile(enabled: boolean): void
  setTypewriterScroll(enabled: boolean, anchor: string): void
  setPageView(enabled: boolean): void
  setBookParagraphSpacing(enabled: boolean): void
  setCommentsData(data: { id: string; anchorText: string; text: string }[]): void
  setFootnotesData(data: { id: string; text: string }[]): void
  setEntityNames(namesJson: string): void
  setMentionCandidates(candidatesJson: string): void
  setMentionLabels(labelsJson: string): void
  /** Answers a `mentionCreateRequested` message: turns the pending placeholder
   *  into a real mention, or back into plain text when entityId is null. */
  resolvePendingMention(
    pendingId: string,
    entityId: string | null,
    entityType: string | null,
    displayText: string | null
  ): void
  setAutoReplacements(pairsJson: string): void
  setDialogueCorrectionConfig(configJson: string): void
  setContextMenuLabels(labelsJson: string): void
  /**
   * Turns typing into suggesting: new words go in as insertions and deleted
   * ones are marked rather than removed, so an editor proposes a change
   * instead of making one.
   */
  setSuggestionMode(on: boolean, author: string): void
  setInlineActions(actionsJson: string): void
  setExtensionContextMenuItems(itemsJson: string): void
  applyInlineActionResult(resultJson: string): void
  setGrammarCheckEnabled(enabled: boolean): void
  setGrammarIssues(issuesJson: string): void
  addCommentToSelection(id: string): void
  /** Turns the selection into a link, or unlinks it when the address is empty. */
  applyLink(href: string): void
  /** Dims every paragraph but the one the caret is in. */
  setComposeDimming(enabled: boolean): void
  /** The book's own completion list, and how many characters trigger it. */
  setCompletionList(words: string[], trigger: number): void
  removeCommentById(id: string): void
  scrollToCommentById(id: string): void
  insertFootnoteAtSelection(id: string): void
  removeFootnoteById(id: string): void
  scrollToFootnoteById(id: string): void
  toggleBold(): void
  toggleItalic(): void
  toggleUnderline(): void
  alignLeft(): void
  alignCenter(): void
  alignRight(): void
  alignJustify(): void
  /** Applies a named paragraph style (`nv-style-<id>`) to the selection; '' clears it. */
  setParagraphStyle(style: string): void
  toggleBulletList(): void
  toggleNumberList(): void
  focusEditor(): void
  /** Chromium's spelling suggestions for the word the menu was opened on. */
  setSpellingSuggestions(word: string, suggestions: string[]): void
}

export interface EditorMessage {
  type: string
  [key: string]: unknown
}

export type EditorMessageHandler = (message: EditorMessage) => void

/** Subscribes to messages from a specific editor iframe. Returns a disposer. */
export function listenToEditor(
  iframe: HTMLIFrameElement,
  handler: EditorMessageHandler
): () => void {
  const onMessage = (event: MessageEvent): void => {
    if (event.source !== iframe.contentWindow) return
    const raw = (event.data as { novalistEditor?: string })?.novalistEditor
    if (typeof raw !== 'string') return
    try {
      handler(JSON.parse(raw) as EditorMessage)
    } catch {
      // Malformed frame payloads are dropped; the editor only sends JSON.
    }
  }
  window.addEventListener('message', onMessage)
  return () => window.removeEventListener('message', onMessage)
}

export function editorWindow(iframe: HTMLIFrameElement): EditorWindow | null {
  return iframe.contentWindow as EditorWindow | null
}

// ── Inline-action registry (SDK extension surface) ─────────────────────────
// The editor.html context menu can offer extension-contributed "inline actions"
// over a selection. The old Avalonia host fed these from IInlineActionContributor
// and executed them through the extension host. The Electron backend exposes no
// inline-action RPC yet, so this client-side registry is the plumbing: it keeps
// the editor.html hooks (setInlineActions / inlineActionRequested /
// applyInlineActionResult) live and gives extensions a real place to register.

export interface InlineActionDescriptor {
  id: string
  label: string
  group?: string
  icon?: string
  /** Offered at a bare caret and listed in the slash menu. */
  allowsEmptySelection?: boolean
  /** What the writer types after the slash, without the slash. */
  slashKeyword?: string
}

export interface InlineActionResult {
  /** Text to insert. */
  text: string
  /** 'replace' swaps the selection, 'insertAfter' appends on a new line after
   *  it, 'insertAtCaret' writes at the caret and replaces nothing. */
  disposition: 'replace' | 'insertAfter' | 'insertAtCaret'
  /** Non-empty aborts the edit; editor.html leaves the selection untouched. */
  error?: string
}

/** What the editor knows about where the action was invoked. */
export interface InlineActionContext {
  /** Prose before the selection, or before the caret when nothing is selected. */
  precedingText?: string
  /** What was typed after the slash, when the action came from the slash menu. */
  directive?: string
}

export type InlineActionHandler = (
  selectedText: string,
  context?: InlineActionContext
) => InlineActionResult | Promise<InlineActionResult>

const inlineActions = new Map<
  string,
  { descriptor: InlineActionDescriptor; run: InlineActionHandler }
>()

/** Registers (or replaces) an inline action. Returns a disposer. */
export function registerInlineAction(
  descriptor: InlineActionDescriptor,
  run: InlineActionHandler
): () => void {
  inlineActions.set(descriptor.id, { descriptor, run })
  return () => inlineActions.delete(descriptor.id)
}

/** JSON array of registered descriptors, in the shape editor.html expects. */
export function inlineActionDescriptorsJson(): string {
  return JSON.stringify([...inlineActions.values()].map((a) => a.descriptor))
}

/** Runs an inline action by id; unknown ids resolve to an error result. */
export async function runInlineAction(
  actionId: string,
  selectedText: string,
  context?: InlineActionContext
): Promise<InlineActionResult> {
  const entry = inlineActions.get(actionId)
  if (!entry) {
    return { text: '', disposition: 'replace', error: `Unknown inline action: ${actionId}` }
  }
  try {
    return await entry.run(selectedText, context)
  } catch (err) {
    return { text: '', disposition: 'replace', error: String(err) }
  }
}

// ── Extension context-menu items (IContextMenuContributor) ─────────────────
// Separate from inline actions: these are not selection-gated and operate on the
// currently open scene (Context "Scene"/"Editor"). The backend owns the click
// handler; the editor only needs the descriptors and posts the chosen id back.

export interface ExtensionContextMenuItem {
  id: string
  label: string
  icon?: string
}

let extensionContextMenuItems: ExtensionContextMenuItem[] = []

/** Replaces the extension context-menu descriptor list pushed to the editor. */
export function setExtensionContextMenuItems(items: ExtensionContextMenuItem[]): void {
  extensionContextMenuItems = items
}

/** JSON array of extension context-menu descriptors, in editor.html's shape. */
export function extensionContextMenuItemsJson(): string {
  return JSON.stringify(extensionContextMenuItems)
}

/** Reads the current theme tokens and pushes them into the editor page.
 *  The iframe is a separate document, so browser-painted chrome (scrollbars)
 *  needs the resolved colours handed over explicitly. */
export function pushEditorTheme(editor: EditorWindow): void {
  const style = getComputedStyle(document.documentElement)
  const token = (name: string): string => style.getPropertyValue(name).trim()
  const fg = token('--nl-text')
  editor.setTheme(
    token('--nl-surface-editor'),
    fg,
    fg,
    token('--nl-surface-selected'),
    token('--nl-surface-card'),
    fg,
    token('--nl-scrollbar-thumb'),
    token('--nl-scrollbar-thumb-hover'),
    token('--nl-scrollbar-thumb-active')
  )
}
