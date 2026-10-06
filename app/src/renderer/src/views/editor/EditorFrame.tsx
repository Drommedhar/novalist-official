import { useEditorMessages } from './useEditorMessages'
import { type CSSProperties, useState } from 'react'
import { EditorToolbar } from './EditorToolbar'
import { useShellStore } from '../../stores/shellStore'
import { EntityTypeDialog } from '../../shell/EntityTypeDialog'
import { AppendToEntityDialog } from '../../shell/AppendToEntityDialog'
import { InputDialog } from '../../shell/InputDialog'
import './editor.css'
import { useEditorFrameModel } from './useEditorFrameModel'
import { useEditorPersistence, useEditorScene, useEditorControls } from './useEditorSynchronization'
import { useTranslation } from 'react-i18next'
import { editorPane, useProjectStore, type SceneTabRef } from '../../stores/projectStore'

export function EditorFrame({ paneId }: { paneId?: string }): React.JSX.Element {
  const model = useEditorFrameModel({ paneId })
  const { editorFontSize, peekScope, t, pane, formatting, speaking, isActiveEditor, showFocusPeekTip, editorRef, completeFocusPeekTip, dismissFocusPeekTip, iframeRef, peek, pendingEntity, createPendingEntity, cancelPendingEntity, pendingAppend, appendSelectionToEntity, setPendingAppend, linkPrompt, setLinkPrompt, pendingImage, setPendingImage, lastReportedHtmlRef, loadingRef, loadAnnotations, applyFootnoteOrder, pushEntityNames, peekRef, i18n, sceneTitleRef, annotationsRef, persistAnnotations, pushAnnotations, paneIds, setPendingEntity, entityIndexRef, setSpeaking, setFormatting } = model
  useEditorPersistence(model)
  useEditorScene(model)
  useEditorMessages({
    iframeRef, pane, peekRef, editorRef, i18n, loadingRef, lastReportedHtmlRef, loadAnnotations, applyFootnoteOrder, pushEntityNames, t, setPendingImage, sceneTitleRef, setLinkPrompt, annotationsRef, persistAnnotations, pushAnnotations, paneIds, setPendingAppend, setPendingEntity, entityIndexRef, setSpeaking, setFormatting
  })
  useEditorControls(model)
  return (
    <div
      className="editor-pane"
      style={{ '--nl-focus-page-scale': editorFontSize / 17 } as CSSProperties}
    >
      {!window.novalist.isMobile && (
        <div className="editor-scene-heading">
          <div>
            <strong>{peekScope.sceneTitle}</strong>
            <small>{peekScope.chapterTitle}</small>
          </div>
          <button
            className="dialog-button"
            onClick={() => useShellStore.getState().toggleFocusMode()}
          >
            {t('menu.focusMode')}
          </button>
          <button
            className="dialog-button"
            onClick={() => useShellStore.getState().toggleInspector()}
          >
            {t('desktopRefresh.sceneDetails')}
          </button>
        </div>
      )}
      <SceneTabStrip paneId={pane} />
      <EditorToolbar formatting={formatting} speaking={speaking} active={isActiveEditor} />
      {isActiveEditor && formatting.entityAtCaret && showFocusPeekTip && (
        <section
          className="editor-onboarding-tip"
          aria-label={t('focusPeek.tipTitle')}
          data-onboarding-tip="focus-peek"
        >
          <div className="editor-onboarding-copy">
            <strong>{t('focusPeek.tipTitle')}</strong>
            <span>{t('focusPeek.tipBody')}</span>
          </div>
          <div className="editor-onboarding-actions">
            <button
              className="btn-primary"
              onClick={() => {
                if (editorRef.current?.peekEntityAtCaret()) completeFocusPeekTip('focus-peek')
              }}
            >
              {t('focusPeek.tipTry')}
            </button>
            <button className="btn-secondary" onClick={() => dismissFocusPeekTip('focus-peek')}>
              {t('focusPeek.tipDismiss')}
            </button>
          </div>
        </section>
      )}
      <iframe
        ref={iframeRef}
        className="editor-frame"
        src="./editor/editor.html"
        title="editor"
        sandbox="allow-scripts allow-same-origin"
      />
      {peek.overlay}
      {pendingEntity && (
        <EntityTypeDialog
          name={pendingEntity.name}
          onPick={(typeKey) => void createPendingEntity(typeKey)}
          onCancel={cancelPendingEntity}
        />
      )}
      {pendingAppend != null && (
        <AppendToEntityDialog
          text={pendingAppend}
          onConfirm={(target) => void appendSelectionToEntity(target)}
          onCancel={() => setPendingAppend(null)}
        />
      )}
      {/* The frame owns no dialogs, so it asks and the host answers. An empty
          address unlinks, which is how a link is taken off again. */}
      {linkPrompt && (
        <InputDialog
          title={t('editor.linkPrompt')}
          placeholder="https://"
          onCancel={() => setLinkPrompt(false)}
          onSubmit={(value) => {
            setLinkPrompt(false)
            editorRef.current?.applyLink(value.trim())
          }}
        />
      )}

      {/* Alt text, asked for at insert time. Asking later means never, and a
          picture nobody described is invisible to a reader who cannot see it. */}
      {pendingImage && (
        <InputDialog
          title={t('editorImage.altTitle')}
          placeholder={t('editorImage.altPlaceholder')}
          onCancel={() => {
            // Cancelling still places the image; a writer who does not want to
            // describe it should not lose the insert over it.
            const path = pendingImage
            setPendingImage(null)
            editorRef.current?.insertImageAtCaret(path, '')
          }}
          onSubmit={(value) => {
            const path = pendingImage
            setPendingImage(null)
            editorRef.current?.insertImageAtCaret(path, value.trim())
          }}
        />
      )}
    </div>
  )
}

/** Ordered tab strip for the scenes open in one editor pane. */
export function SceneTabStrip({ paneId }: { paneId: string }): React.JSX.Element | null {
  const { t } = useTranslation()
  const tabs = useProjectStore((s) => editorPane(s, paneId).tabs)
  const activeId = useProjectStore((s) => editorPane(s, paneId).sceneId)
  const chapters = useProjectStore((s) => s.chapters)
  const dirtyMap = useProjectStore((s) => s.dirtyMap)
  const editorCount = useProjectStore(
    (s) => Object.values(s.editors).filter((e) => e.sceneId !== null).length
  )
  const [menu, setMenu] = useState<{ x: number; y: number; sceneId: string } | null>(null)

  // With a second editor open every pane shows its strip, so each side's scene
  // is closeable and the panes look alike. A lone editor keeps the strip-free
  // look until a second scene is opened in it.
  if (tabs.length === 0) return null
  if (editorCount < 2 && tabs.length <= 1) return null

  const titleFor = (ref: SceneTabRef): string => {
    const chapter = chapters.find((c) => c.guid === ref.chapterGuid)
    const scene = chapter?.scenes.find((s) => s.id === ref.sceneId)
    return scene?.title || chapter?.title || ''
  }

  const activate = (ref: SceneTabRef): void => {
    if (ref.sceneId === activeId) return
    void useProjectStore.getState().openSceneIn(paneId, ref.chapterGuid, ref.sceneId)
  }
  const close = (sceneId: string): void => {
    void useProjectStore.getState().closeTab(paneId, sceneId)
  }
  const moveOther = (sceneId: string): void => {
    setMenu(null)
    void useProjectStore.getState().moveTabToOtherPane(paneId, sceneId)
  }

  return (
    <div className="editor-tabs" role="tablist">
      {tabs.map((ref) => (
        <div
          key={ref.sceneId}
          className={`editor-tab${ref.sceneId === activeId ? ' active' : ''}`}
          role="tab"
          aria-selected={ref.sceneId === activeId}
          title={titleFor(ref)}
          onClick={() => activate(ref)}
          onAuxClick={(e) => {
            if (e.button === 1) {
              e.preventDefault()
              close(ref.sceneId)
            }
          }}
          onContextMenu={(e) => {
            e.preventDefault()
            setMenu({ x: e.clientX, y: e.clientY, sceneId: ref.sceneId })
          }}
        >
          {dirtyMap[ref.sceneId] && <span className="editor-tab-dirty" aria-hidden="true" />}
          <span className="editor-tab-title">{titleFor(ref)}</span>
          <button
            className="editor-tab-close"
            aria-label={t('editor.tabClose')}
            onClick={(e) => {
              e.stopPropagation()
              close(ref.sceneId)
            }}
          >
            ×
          </button>
        </div>
      ))}
      {menu && (
        <>
          <div
            className="editor-tab-menu-scrim"
            onClick={() => setMenu(null)}
            onContextMenu={(e) => {
              e.preventDefault()
              setMenu(null)
            }}
          />
          <div className="editor-tab-menu" style={{ left: menu.x, top: menu.y }}>
            <button
              onClick={() => {
                close(menu.sceneId)
                setMenu(null)
              }}
            >
              {t('editor.tabClose')}
            </button>
            <button onClick={() => moveOther(menu.sceneId)}>{t('editor.tabMoveOther')}</button>
          </div>
        </>
      )}
    </div>
  )
}
