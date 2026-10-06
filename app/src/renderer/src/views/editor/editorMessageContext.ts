import type { RefObject } from 'react'
import type { i18n, TFunction } from 'i18next'
import type { EditorMessageHandler, EditorWindow } from './editorBridge'
import type { FormattingState } from './EditorToolbar'
import type { EntityHit } from './editorEntities'
import type { EntityPeekController } from './peekTypes'
import type { EditorAnnotations } from './useEditorAnnotations'

export interface EditorMessageContext extends Pick<EditorAnnotations,
  'annotationsRef' | 'loadAnnotations' | 'applyFootnoteOrder' | 'persistAnnotations' | 'pushAnnotations' | 'paneIds'> {
  iframeRef: RefObject<HTMLIFrameElement | null>
  editorRef: RefObject<EditorWindow | null>
  pane: string
  peekRef: RefObject<EntityPeekController>
  i18n: i18n
  t: TFunction
  loadingRef: RefObject<boolean>
  lastReportedHtmlRef: RefObject<string | null>
  sceneTitleRef: RefObject<string | null>
  entityIndexRef: RefObject<Map<string, EntityHit>>
  pushEntityNames(editor: EditorWindow): Promise<void>
  setPendingImage(value: string | null): void
  setLinkPrompt(value: boolean): void
  setPendingAppend(value: string | null): void
  setPendingEntity(value: { name: string; pendingId: string } | null): void
  setSpeaking(value: boolean): void
  setFormatting(value: FormattingState): void
}

export interface EditorHandlerContext extends EditorMessageContext {
  iframe: HTMLIFrameElement
  activatePane(): void
  showHoverCard(
    hit: { id: string; type: string }, x: number, y: number,
    wordRect?: { left: number; top: number; right: number; bottom: number } | null
  ): void
}

export type EditorMessageHandlers = Record<string, EditorMessageHandler>
