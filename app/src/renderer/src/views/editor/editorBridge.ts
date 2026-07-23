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
  setLanguage(lang: string): void
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
  setInlineActions(actionsJson: string): void
  setExtensionContextMenuItems(itemsJson: string): void
  applyInlineActionResult(resultJson: string): void
  setGrammarCheckEnabled(enabled: boolean): void
  setGrammarIssues(issuesJson: string): void
  addCommentToSelection(id: string): void
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
  focusEditor(): void
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
}

export interface InlineActionResult {
  /** Text to insert. */
  text: string
  /** 'replace' swaps the selection; 'insertAfter' appends on a new line. */
  disposition: 'replace' | 'insertAfter'
  /** Non-empty aborts the edit; editor.html leaves the selection untouched. */
  error?: string
}

export type InlineActionHandler = (
  selectedText: string
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
  selectedText: string
): Promise<InlineActionResult> {
  const entry = inlineActions.get(actionId)
  if (!entry) {
    return { text: '', disposition: 'replace', error: `Unknown inline action: ${actionId}` }
  }
  try {
    return await entry.run(selectedText)
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
