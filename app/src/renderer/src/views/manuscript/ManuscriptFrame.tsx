import { useEffect, useRef } from 'react'
import { useManuscriptStore } from '../../stores/manuscriptStore'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore, type EffectiveSettings } from '../../stores/settingsStore'
import { registerPendingWrite } from '../../stores/pendingWrites'
import { pushWritingTypography } from '../editor/writingTypography'
import { useWritingZoomStore } from '../../stores/writingZoomStore'
import { SharedManuscriptEditors, type ManuscriptEditingWindow } from './SharedManuscriptEditors'
import { rpc } from '../../rpc/client'
import { useHostBridgeStore } from '../../stores/hostBridgeStore'
import { useTranslation } from 'react-i18next'
import './manuscript-shared.css'

export const EAGER_SECTIONS = 3

interface ManuscriptWindow extends ManuscriptEditingWindow {
  setManuscript(sectionsJson: string): void
  setTheme(
    bg: string,
    fg: string,
    caret: string,
    selectionBg: string,
    accent: string,
    subtle: string,
    divider: string,
    scrollbarThumb?: string,
    scrollbarThumbHover?: string,
    scrollbarThumbActive?: string
  ): void
  setFont(family: string, size: number): void
  setBookWidth(enabled: boolean, widthPx: number): void
  setWritingZoom(factor: number, typewriterAnchor?: string | null): void
  setReadingComfort(lineHeight: number, letterSpacing: number, firstLineIndent: number): void
  flushPendingChanges(): Array<{
    sceneId: string
    chapterGuid: string
    html: string
    plainText: string
    wordCount: number
  }>
}

function presentManuscript(win: ManuscriptWindow, eff?: EffectiveSettings): void {
  const style = getComputedStyle(document.documentElement)
  const token = (name: string): string => style.getPropertyValue(name).trim()
  for (const name of style) {
    if (name.startsWith('--nl-') || name.startsWith('--nv-')) {
      win.document.documentElement.style.setProperty(name, token(name))
    }
  }
  win.setTheme(
    token('--nl-surface-editor'),
    token('--nl-text'),
    token('--nl-text'),
    token('--nl-surface-selected'),
    token('--nl-accent'),
    token('--nl-text-subtle'),
    token('--nl-border'),
    // Separate document: browser-painted scrollbars need the colours pushed.
    token('--nl-scrollbar-thumb'),
    token('--nl-scrollbar-thumb-hover'),
    token('--nl-scrollbar-thumb-active')
  )
  // Manuscript mode is the same prose in a longer strip, so it reads with the
  // writer's own face and leading rather than the page's defaults.
  if (eff) {
    pushWritingTypography(win, eff)
    win.setReadingComfort(
      eff.editorLineHeight,
      eff.editorLetterSpacing,
      eff.editorFirstLineIndent
    )
  }
}

export function ManuscriptFrame(): React.JSX.Element {
  const { t } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const sharedRef = useRef<SharedManuscriptEditors | null>(null)
  const readyRef = useRef(false)
  const sections = useManuscriptStore((s) => s.sections)
  const effective = useSettingsStore((s) => s.view?.effective)
  const zoomPercent = useWritingZoomStore((s) => s.percent)
  const sharedError = useManuscriptStore(s => Object.values(s.sharedEdits).some(edit => edit.error))
  const sectionsRef = useRef(sections)
  const effectiveRef = useRef(effective)
  sectionsRef.current = sections
  effectiveRef.current = effective

  const pushPresentation = (): void => {
    const win = iframeRef.current?.contentWindow as ManuscriptWindow | null
    if (!win || !readyRef.current) return
    presentManuscript(win, effectiveRef.current)
  }

  const pushContent = (): void => {
    const win = iframeRef.current?.contentWindow as ManuscriptWindow | null
    if (!win || !readyRef.current) return
    const payload = sectionsRef.current.map((s) => ({
      chapterGuid: s.chapterGuid,
      chapterTitle: s.chapterTitle,
      status: s.status,
      act: s.act,
      scenes: s.scenes.map((sc) => ({
        sceneId: sc.sceneId,
        title: sc.title,
        html: sc.html,
        shared: !!sc.shared,
        wordCount: sc.wordCount
      }))
    }))
    sharedRef.current?.prepare(sectionsRef.current)
    win.setManuscript(JSON.stringify(payload))
    sharedRef.current?.sync(sectionsRef.current, win)
  }

  const push = (): void => {
    pushPresentation()
    pushContent()
  }

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return
    const store = useManuscriptStore.getState()
    sharedRef.current = new SharedManuscriptEditors()

    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframe.contentWindow) return
      const raw = (event.data as { novalistManuscript?: string })?.novalistManuscript
      if (typeof raw !== 'string') return
      let message: { type: string; [key: string]: unknown }
      try {
        message = JSON.parse(raw)
      } catch {
        return
      }
      switch (message.type) {
        case 'zoom':
          useWritingZoomStore.getState().change(Number(message.delta ?? 0))
          break
        case 'ready':
          readyRef.current = true
          push()
          void rpc.request<string>('gallery/base').then(base => {
            const win = iframe.contentWindow as ManuscriptWindow | null
            win?.setImageBase(`novalist-project://nl/${base}`)
          }).catch(error => useHostBridgeStore.getState().pushToast(String(error)))
          break
        case 'sceneContentChanged':
          if (sharedRef.current?.capture(String(message.sceneId))) break
          store.onSceneContentChanged(
            String(message.sceneId),
            String(message.html ?? ''),
            String(message.plainText ?? ''),
            Number(message.wordCount ?? 0)
          )
          break
        case 'cycleStatus':
          void store.cycleStatus(String(message.chapterGuid))
          break
        case 'sceneFocused':
          // The page has reported this since it was written and the host had
          // no case for it, so the context sidebar kept describing whichever
          // scene was last opened from the binder while the writer typed in
          // another one.
          useProjectStore
            .getState()
            .setContextScene(String(message.chapterGuid), String(message.sceneId))
          break
        case 'openScene':
          void useProjectStore
            .getState()
            .openScene(String(message.chapterGuid), String(message.sceneId))
          break
        default:
          break
      }
    }

    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
      sharedRef.current?.close()
      sharedRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const capture = (): void => {
      const win = iframeRef.current?.contentWindow as ManuscriptWindow | null
      const store = useManuscriptStore.getState()
      for (const change of win?.flushPendingChanges() ?? []) {
        if (!sharedRef.current?.capture(change.sceneId)) store.onSceneContentChanged(change.sceneId, change.html, change.plainText, change.wordCount)
      }
      sharedRef.current?.capture()
    }
    return registerPendingWrite(async () => {
      capture()
      await sharedRef.current?.flush()
      await useManuscriptStore.getState().flushPendingSave()
    }, capture)
  }, [])

  // The refs make the iframe's one-shot ready message use the newest snapshot
  // rather than the empty arrays captured on the component's first render.
  useEffect(() => {
    pushContent()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sections])

  // Typography changes are non-destructive: do not rebuild every scene (and
  // its caret/undo history) merely to change one CSS custom property.
  useEffect(() => {
    pushPresentation()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effective, zoomPercent])

  return (
    <>
      {sharedError && <div role="alert" className="manuscript-shared-error">
        {t('collaboration.editorError')}
        <button type="button" className="dialog-btn" onClick={() => void sharedRef.current?.flush().catch(error => useHostBridgeStore.getState().pushToast(String(error)))}>{t('collaboration.retryField')}</button>
      </div>}
      <iframe
        ref={iframeRef}
        className="editor-frame"
        src="./editor/manuscript-editor.html"
        title="manuscript"
        sandbox="allow-scripts allow-same-origin"
      />
    </>
  )
}
