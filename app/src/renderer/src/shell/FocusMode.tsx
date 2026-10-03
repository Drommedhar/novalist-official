import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { BookOpen, Maximize2, PanelLeft, PanelRight, PenLine, Settings2, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { runCommand } from './commands'
import { Binder } from './Binder'
import { Inspector } from './Inspector'
import { SceneNotesDock } from './SceneNotesDock'
import './focus-mode.css'

/** Return keyboard focus without rebuilding the editor or changing its selection. */
export function focusManuscript(): void {
  requestAnimationFrame(() => {
    // Closing a panel schedules this for the next frame. Navigation may have
    // happened meanwhile; focusing the editor would reactivate its pane.
    const shell = useShellStore.getState()
    if (shell.mainView !== 'write' || shell.extView) return
    const frames = [...document.querySelectorAll<HTMLIFrameElement>('iframe.editor-frame')]
    frames.find((frame) => frame.getBoundingClientRect().width > 0)?.focus()
    useEditorBridge.getState().editor?.focusEditor()
  })
}

export function FocusMode(): React.JSX.Element {
  const { t } = useTranslation()
  const panel = useShellStore((s) => s.focusPanel)
  const transient = useShellStore((s) => s.focusPanelTransient)
  const toolsVisible = useShellStore((s) => s.focusToolsVisible)
  const writing = useShellStore((s) => s.mainView === 'write' && !s.extView)
  const sceneId = useProjectStore((s) => s.openSceneId)
  const chapterGuid = useProjectStore((s) => s.openChapterGuid)
  const sceneKey = `${chapterGuid}/${sceneId}`
  const [readyScene, setReadyScene] = useState<string | null>(null)
  const inspectorReady = useCallback((ready: boolean) => setReadyScene(ready ? sceneKey : null), [sceneKey])
  const shownPanel = panel === 'inspector' && readyScene !== sceneKey ? null : panel
  const chapters = useProjectStore((s) => s.chapters)
  const scene = chapters.flatMap((chapter) => chapter.scenes).find((item) => item.id === sceneId)
  const overlay = useRef<HTMLDivElement>(null)
  const bar = useRef<HTMLDivElement>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const revealTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const retreatTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const dismissedEdge = useRef<'binder' | 'inspector' | null>(null)
  const cancelReveal = (): void => {
    if (revealTimer.current) clearTimeout(revealTimer.current)
  }
  const cancelRetreat = (): void => {
    if (retreatTimer.current) clearTimeout(retreatTimer.current)
  }
  const editingPanel = (): boolean => {
    const active = document.activeElement
    return !!active && !!overlay.current?.contains(active) && active.matches('input, textarea, select, [contenteditable="true"]')
  }
  const retreatSoon = (): void => {
    cancelRetreat()
    retreatTimer.current = setTimeout(() => {
      if (!useShellStore.getState().focusPanelTransient || editingPanel()) return
      const hadFocus = !!overlay.current?.contains(document.activeElement)
      useShellStore.getState().closeFocusPanel()
      if (hadFocus) focusManuscript()
    }, 300)
  }
  const revealSoon = (side: 'binder' | 'inspector'): void => {
    cancelReveal()
    cancelRetreat()
    if (dismissedEdge.current === side) return
    revealTimer.current = setTimeout(() => {
      if (!editingPanel()) useShellStore.getState().revealFocusPanel(side)
    }, 160)
  }
  const close = (): void => {
    cancelReveal()
    cancelRetreat()
    const current = useShellStore.getState().focusPanel
    // The closing panel exposes the edge underneath a stationary pointer.
    // Escape must stay dismissed until the pointer leaves and returns.
    if (current === 'binder' || current === 'inspector') {
      const edge = document.querySelector(`.focus-edge-${current === 'binder' ? 'left' : 'right'}`)
      dismissedEdge.current = overlay.current?.matches(':hover') || edge?.matches(':hover') ? current : null
    }
    useShellStore.getState().closeFocusPanel()
    focusManuscript()
  }

  useEffect(() => {
    void window.novalist.setFocusWindow?.(true)
    return () => { void window.novalist.setFocusWindow?.(false) }
  }, [])

  // Measure the actual page, so hover targets follow zoom, UI scale and display size.
  // Observing sizes avoids work on every pointer move or editor keystroke.
  useLayoutEffect(() => {
    // Other workspaces can change Codex entries and scene metadata. Prepare a
    // fresh context on return, while retaining it across ordinary panel hovers.
    if (!writing) {
      setReadyScene(null)
      return
    }
    const shell = bar.current?.closest<HTMLElement>('.shell')
    if (!shell) return
    const frames = [...shell.querySelectorAll<HTMLIFrameElement>('iframe.editor-frame')]
    const measure = (): void => {
      const page = frames.map((frame) => frame.getBoundingClientRect()).find((rect) => rect.width > 0)
      if (!page) return
      const bounds = shell.getBoundingClientRect()
      const minimum = parseFloat(getComputedStyle(shell).getPropertyValue('--nl-focus-edge-width'))
      shell.style.setProperty('--nl-focus-hover-left', `${Math.max(minimum, page.left - bounds.left)}px`)
      shell.style.setProperty('--nl-focus-hover-right', `${Math.max(minimum, bounds.right - page.right)}px`)
    }
    const observer = new ResizeObserver(measure)
    observer.observe(shell)
    frames.forEach((frame) => observer.observe(frame))
    measure()
    return () => {
      observer.disconnect()
      shell.style.removeProperty('--nl-focus-hover-left')
      shell.style.removeProperty('--nl-focus-hover-right')
    }
  }, [writing, sceneId])

  useEffect(() => {
    if (shownPanel && writing && !transient) overlay.current?.focus()
    return () => {
      const shell = useShellStore.getState()
      if (panel && !transient && shell.focusMode && shell.mainView === 'write' && !shell.focusPanel) focusManuscript()
    }
  }, [panel, shownPanel, transient, writing])

  useEffect(() => {
    const onKey = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape' || event.defaultPrevented) return
      if (useShellStore.getState().focusPanel) {
        event.preventDefault()
        close()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
      if (timer.current) clearTimeout(timer.current)
      cancelReveal()
      cancelRetreat()
    }
  }, [])

  const hideSoon = (): void => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      const active = document.activeElement
      if (!bar.current?.contains(active) && !active?.closest('.editor-toolbar')) {
        useShellStore.getState().setFocusToolsVisible(false)
      }
    }, 1200)
  }

  if (!writing) return (
    <button className="focus-return" onClick={() => {
      useShellStore.getState().returnToFocus()
      focusManuscript()
    }}>
      <BookOpen size={16} /> {t('focus.returnToPage')}
    </button>
  )

  return <>
    {(['binder', 'inspector'] as const).map((side) => <button key={side}
      className={`focus-edge focus-edge-${side === 'binder' ? 'left' : 'right'}`}
      aria-label={t(`focus.${side}`)} aria-expanded={shownPanel === side}
      aria-busy={side === 'inspector' && panel === side && !shownPanel}
      onPointerEnter={(event) => { if (event.pointerType === 'mouse') revealSoon(side) }}
      onPointerLeave={() => {
        if (dismissedEdge.current === side) dismissedEdge.current = null
        cancelReveal(); retreatSoon()
      }}
      onClick={() => {
        dismissedEdge.current = null
        cancelReveal(); cancelRetreat()
        if (panel === side && transient) useShellStore.setState({ focusPanelTransient: false })
        else runCommand(side === 'binder' ? 'app.toggleBinder' : 'app.toggleInspector')
      }}>
      {side === 'binder' ? <PanelLeft size={16} /> : <PanelRight size={16} />}
    </button>)}
    <div ref={bar} className="focus-bar" onMouseEnter={() => {
      if (timer.current) clearTimeout(timer.current)
      useShellStore.getState().setFocusToolsVisible(true)
    }} onMouseLeave={hideSoon} onBlur={hideSoon}>
      <button className="focus-reveal" aria-label={t('focus.tools')} aria-expanded={toolsVisible}
        onFocus={() => useShellStore.getState().setFocusToolsVisible(true)}
        onClick={() => useShellStore.getState().setFocusToolsVisible(true)}>
        <Settings2 size={16} />
      </button>
      <div className="focus-bar-tools" inert={!toolsVisible}>
        <span className="focus-scene-title">{scene?.title ?? t('shell.view.write')}</span>
        {scene && <span className="focus-word-count">{t('focus.words', { count: scene.wordCount })}</span>}
        <button title={t('shell.toggleBinder')} aria-label={t('shell.toggleBinder')}
          onClick={() => runCommand('app.toggleBinder')}><PanelLeft size={17} /></button>
        <button title={t('shell.toggleSceneNotes')} aria-label={t('shell.toggleSceneNotes')}
          onClick={() => runCommand('app.toggleSceneNotes')}><PenLine size={17} /></button>
        <button title={t('shell.toggleInspector')} aria-label={t('shell.toggleInspector')}
          onClick={() => runCommand('app.toggleInspector')}><PanelRight size={17} /></button>
        <button className="focus-exit" onClick={() => {
          useShellStore.getState().toggleFocusMode()
          focusManuscript()
        }}><Maximize2 size={16} /> {t('focus.exit')}</button>
      </div>
    </div>
    <div className="focus-overlay">
      {shownPanel && !transient && <button className="focus-scrim" tabIndex={-1} aria-label={t('focus.closePanel')} onClick={close} />}
      {(['binder', 'inspector', 'notes'] as const).map((side) => <div key={side}
        ref={shownPanel === side ? overlay : undefined}
        className={`focus-panel focus-panel-${side}${shownPanel === side ? ' focus-panel-open' : ''}`} role="dialog"
        inert={shownPanel !== side} aria-hidden={shownPanel !== side}
        aria-modal={shownPanel === side && !transient} aria-label={t(`focus.${side}`)} tabIndex={-1}
        onPointerEnter={cancelRetreat} onPointerLeave={(event) => {
          const target = event.relatedTarget
          const edge = `.focus-edge-${side === 'binder' ? 'left' : 'right'}`
          if (dismissedEdge.current === side && !(target instanceof Element && target.closest(edge))) {
            dismissedEdge.current = null
          }
          retreatSoon()
        }}
        onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) retreatSoon() }}
        onKeyDown={(event) => {
          if (event.key !== 'Tab' || transient) return
          const controls = [...event.currentTarget.querySelectorAll<HTMLElement>(
            'button:not([disabled]), input, textarea, select, [tabindex="0"]'
          )].filter((item) => item.getBoundingClientRect().height > 0)
          const first = controls[0]
          const last = controls[controls.length - 1]
          if (event.shiftKey && (document.activeElement === first || document.activeElement === overlay.current)) {
            event.preventDefault(); last?.focus()
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault(); first?.focus()
          }
        }}>
        <div className="focus-panel-heading">
          <span>{t(`focus.${side}`)}</span>
          <button aria-label={t('focus.closePanel')} onClick={close}><X size={18} /></button>
        </div>
        {side === 'binder' ? <Binder /> : side === 'inspector'
          ? <Inspector key={sceneKey} onReadyChange={inspectorReady} prepareHidden={shownPanel !== 'inspector'} />
          : panel === 'notes' ? <SceneNotesDock /> : null}
      </div>)}
    </div>
  </>
}
