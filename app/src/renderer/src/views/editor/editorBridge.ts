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
    pageFg: string
  ): void
  setFont(family: string, size: number): void
  setLanguage(lang: string): void
  setTypewriterScroll(enabled: boolean, anchor: string): void
  setPageView(enabled: boolean): void
  setBookParagraphSpacing(enabled: boolean): void
  setCommentsData(data: { id: string; anchorText: string; text: string }[]): void
  setFootnotesData(data: { id: string; text: string }[]): void
  setEntityNames(namesJson: string): void
  setAutoReplacements(pairsJson: string): void
  setDialogueCorrectionConfig(configJson: string): void
  setContextMenuLabels(labelsJson: string): void
  setGrammarCheckEnabled(enabled: boolean): void
  setGrammarIssues(issuesJson: string): void
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

/** Reads the current theme tokens and pushes them into the editor page. */
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
    fg
  )
}
