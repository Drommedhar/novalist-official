import { useEditorAnnotations } from './useEditorAnnotations'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { type EditorWindow } from './editorBridge'
import { type FormattingState } from './EditorToolbar'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { useOnboardingStore } from '../../stores/onboardingStore'
import { editorPane, useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { useEditorEntityRequests } from './useEditorEntityRequests'
import { useEditorPeek } from './useEditorPeek'

const DEFAULT_FORMATTING: FormattingState = {
  bold: false,
  italic: false,
  underline: false,
  strikethrough: false,
  highlight: false,
  alignment: 'left',
  paragraphStyle: '',
  bulletList: false,
  numberList: false,
  hasSelection: false,
  linkActive: false,
  entityAtCaret: false
}

export function useEditorFrameModel({ paneId }: { paneId?: string }) {
  const { t, i18n } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const editorRef = useRef<EditorWindow | null>(null)
  const [linkPrompt, setLinkPrompt] = useState(false)
  // Mobile has no panes, so an editor mounted without one is whichever the
  // shell is following.
  const fallbackPaneId = useProjectStore((s) => s.activeEditorPaneId)
  const pane = paneId ?? fallbackPaneId ?? ''
  const openSceneId = useProjectStore((s) => editorPane(s, pane).sceneId)
  const sceneHtml = useProjectStore((s) => editorPane(s, pane).html)
  const activeEditorPane = useProjectStore((s) => s.activeEditorPaneId === pane)
  const writingHere = useShellStore((s) => s.mainView === 'write' && !s.extView)
  const isActiveEditor = activeEditorPane && writingHere
  const loadingRef = useRef(false)
  // The HTML the editor last reported to the store. The store round-trips every
  // keystroke back into sceneHtml, so without this the push effect below would
  // re-setContent on every keystroke - resetting the caret to the start and
  // wiping the native undo stack. We only push content the editor did NOT author.
  const lastReportedHtmlRef = useRef<string | null>(null)
  const [formatting, setFormatting] = useState<FormattingState>(DEFAULT_FORMATTING)
  const [speaking, setSpeaking] = useState(false)
  const suggestionMode = useShellStore((s) => s.suggestionMode)
  const editorFontSize = useSettingsStore((s) => s.view?.effective.editorFontSize ?? 17)
  // A suggested edit somebody asked to be shown, waiting for its scene.
  const pendingSuggestion = useShellStore((s) => s.pendingSuggestion)
  // Bumped by whoever writes the scene's footnotes or comments, this pane
  // included.
  const annotationsRevision = useEditorBridge((s) => s.annotationsRevision)
  // Whose suggestion it is. The author's own name is the only one Novalist
  // knows; an editor working in someone else's copy sets it in Settings.
  const reviewerName = useSettingsStore((s) => s.view?.effective.reviewerName ?? '')
  /** Starts the scene reading from the caret, or stops a reading in progress. */
  const toggleReadAloud = (): void => {
    const live = editorRef.current
    if (!live) return
    if (speaking) {
      live.stopReadAloud()
      return
    }
    const eff = useSettingsStore.getState().view?.effective
    live.startReadAloud(true, eff?.readAloudRate ?? 1, eff?.readAloudVoiceUri ?? null)
  }
  const showFocusPeekTip = useOnboardingStore(
    (state) => state.tipsEnabled && state.tips['focus-peek'] == null
  )
  const completeFocusPeekTip = useOnboardingStore((state) => state.completeTip)
  const dismissFocusPeekTip = useOnboardingStore((state) => state.dismissTip)
  // An image waiting on its alt text. Asked for at insert time, because asking
  // later means never: a picture in the prose without one is invisible to a
  // reader using a screen reader and to an accessible export.
  const [pendingImage, setPendingImage] = useState<string | null>(null)
  const { entityIndexRef, peekScope, peek, peekRef, sceneTitleRef, pushEntityNames } = useEditorPeek(pane, openSceneId)
  const { pendingEntity, setPendingEntity, pendingAppend, setPendingAppend, createPendingEntity, cancelPendingEntity, appendSelectionToEntity } = useEditorEntityRequests(editorRef, pushEntityNames)
  const annotations = useEditorAnnotations(pane, editorRef)
  const { annotationsRef, ownAnnotationsRef, paneIds, pushAnnotations,
    loadAnnotations, persistAnnotations, applyFootnoteOrder } = annotations

  return { editorFontSize, peekScope, t, pane, formatting, speaking, isActiveEditor, showFocusPeekTip, editorRef, completeFocusPeekTip, dismissFocusPeekTip, iframeRef, peek, pendingEntity, createPendingEntity, cancelPendingEntity, pendingAppend, appendSelectionToEntity, setPendingAppend, linkPrompt, setLinkPrompt, pendingImage, setPendingImage, openSceneId, sceneHtml, lastReportedHtmlRef, loadingRef, loadAnnotations, applyFootnoteOrder, annotationsRevision, ownAnnotationsRef, pendingSuggestion, pushEntityNames, peekRef, i18n, sceneTitleRef, annotationsRef, persistAnnotations, pushAnnotations, paneIds, setPendingEntity, entityIndexRef, setSpeaking, setFormatting, suggestionMode, reviewerName, toggleReadAloud }
}

export type EditorFrameModel = ReturnType<typeof useEditorFrameModel>
