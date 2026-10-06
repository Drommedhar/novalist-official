import { useRef } from 'react'
import { ContextMenu } from './ContextMenu'
import { MotionPresence } from './MotionPresence'
import { useContentTransition } from './useContentTransition'
import { useProjectStore } from '../stores/projectStore'
import { SmartListsPanel } from './SmartListsPanel'
import { CollectionsPanel } from './CollectionsPanel'
import { BookmarksPanel } from './BookmarksPanel'
import { SceneBulkBar } from './SceneBulkBar'
import { useBinderState } from './binderState'
import { binderMenuItems } from './binderMenus'
import { ChevronDown, ChevronRight, FileDown, Plus } from 'lucide-react'

import { MobileBookDraftBar } from './MobileBookDraftBar'

import { savePanelSize, useShellStore } from '../stores/shellStore'

import { PanelResizer } from './PanelResizer'

import { SORT_MODES, type SortMode } from './binderTypes'
import type { BinderState } from './binderState'

import { BinderPinned } from './BinderPinned'
import { BinderChapters } from './BinderChapters'
import { BinderArchive } from './BinderArchive'
import { BinderDialogs } from './BinderDialogs'

export function Binder(): React.JSX.Element {
  const state = useBinderState()
  const { binderWidth, binderTab, chapters, t, menu, setMenu } = state
  const contentRef = useRef<HTMLDivElement>(null)
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
  useContentTransition(contentRef, binderTab)
  return (
    <nav className="binder" style={{ width: binderWidth }}>
      <BinderHeader state={state} />
      <div ref={contentRef} className="binder-tree">
        {binderTab === 'smartLists' && <SmartListsPanel />}
        {binderTab === 'collections' && <CollectionsPanel />}
        {binderTab === 'bookmarks' && <BookmarksPanel />}
        {binderTab === 'chapters' && chapters.length === 0 && (
          <div className="binder-placeholder">{t('shell.binderEmpty')}</div>
        )}
        <BinderPinned state={state} />
        <BinderChapters state={state} />
        <BinderArchive state={state} />
      </div>
      <SceneBulkBar />
      <MotionPresence disabled={suspendMotion}>
        {menu && <ContextMenu x={menu.x} y={menu.y} items={binderMenuItems(state)} onClose={() => setMenu(null)} />}
      </MotionPresence>
      <BinderDialogs state={state} />
    </nav>
  )
}

function BinderHeader({ state }: { state: BinderState }): React.JSX.Element {
  const { t, binderTab, setBinderTab, binderWidth, setBinderWidth, sceneFilter, setSceneFilter, sortMode, setSortMode, plotlineFilter, setPlotlineFilter, isMobile, foldControls, controlsOpen, setControlsOpen, setAddChapterOpen, plotlines, chapters } = state
  return <>
      <PanelResizer
        edge="right"
        width={binderWidth}
        onResize={setBinderWidth}
        onResizeEnd={(px) => savePanelSize({ binderWidth: px })}
      />
      <div className="binder-tabs">
        <button
          className={`binder-tab${binderTab === 'chapters' ? ' active' : ''}`}
          onClick={() => setBinderTab('chapters')}
        >
          {t('shell.chapters')}
        </button>
        <button
          className={`binder-tab${binderTab === 'smartLists' ? ' active' : ''}`}
          onClick={() => setBinderTab('smartLists')}
        >
          {t('smartList.section')}
        </button>
        {/* A list is a query and recomputes; a collection is eight scenes
            somebody gathered by hand, which no query describes. */}
        <button
          className={`binder-tab${binderTab === 'collections' ? ' active' : ''}`}
          onClick={() => setBinderTab('collections')}
        >
          {t('collections.section')}
        </button>
        {/* Beside saved lists because they sit next to each other in the head:
            a list is a query, a bookmark is a place. */}
        <button
          className={`binder-tab${binderTab === 'bookmarks' ? ' active' : ''}`}
          onClick={() => setBinderTab('bookmarks')}
        >
          {t('bookmarks.section')}
        </button>
      </div>
      {/* A scene taken out of the book is still in the plan, so the binder has
          to be able to show it, hide it, or show only the parked ones - which
          is the whole point of a state between keeping and archiving. */}
      {/* On a phone these three rows - the in-book filter, the sort and thread
          pickers, and the book/draft bar - came to a third of the screen before
          the first chapter, on the tab a writer opens most. Folded away, they
          are one row until asked for; the chapters get the rest. Open on the
          desktop, where the pane has the room and hiding them would only cost a
          click. */}
      {foldControls && binderTab === 'chapters' && (
        <button
          type="button"
          className="binder-controls-toggle"
          aria-expanded={controlsOpen}
          onClick={() => setControlsOpen((open) => !open)}
        >
          {controlsOpen ? <ChevronDown size={15} strokeWidth={2} /> : <ChevronRight size={15} strokeWidth={2} />}
          {t('binder.sceneFilter')}
        </button>
      )}
      {binderTab === 'chapters' && (!foldControls || controlsOpen) && (
        <div className="binder-scene-filter">
          {(['active', 'all', 'inactive'] as const).map((mode) => (
            <button
              key={mode}
              className={`binder-filter-chip${sceneFilter === mode ? ' active' : ''}`}
              aria-pressed={sceneFilter === mode}
              title={t('binder.sceneFilter')}
              onClick={() => setSceneFilter(mode)}
            >
              {t(
                mode === 'active'
                  ? 'binder.showActive'
                  : mode === 'all'
                    ? 'binder.showAll'
                    : 'binder.showInactive'
              )}
            </button>
          ))}
        </div>
      )}
      {/* Ordering and threads. Neither changes the book: one is a way of
          looking for a scene, the other a way of following one line through
          it. Reading order and every thread is where the binder starts. */}
      {binderTab === 'chapters' && chapters.length > 0 && (!foldControls || controlsOpen) && (
        <div className="binder-sort-row">
          <select
            className="binder-sort-select"
            aria-label={t('binder.sortBy')}
            title={t('binder.sortBy')}
            value={sortMode}
            onChange={(e) => setSortMode(e.target.value as SortMode)}
          >
            {SORT_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {t(`binder.sort_${mode}`)}
              </option>
            ))}
          </select>
          {plotlines.length > 0 && (
            <select
              className="binder-sort-select"
              aria-label={t('binder.threadFilter')}
              title={t('binder.threadFilter')}
              value={plotlineFilter}
              onChange={(e) => setPlotlineFilter(e.target.value)}
            >
              <option value="">{t('binder.allThreads')}</option>
              {plotlines.map((line) => (
                <option key={line.id} value={line.id}>
                  {line.name}
                </option>
              ))}
            </select>
          )}
        </div>
      )}
      {isMobile && binderTab === 'chapters' && (!foldControls || controlsOpen) && <MobileBookDraftBar />}
      {isMobile && binderTab === 'chapters' && (
        <div className="binder-mobile-actions">
          <button className="binder-mobile-add" onClick={() => setAddChapterOpen(true)}>
            <Plus size={15} strokeWidth={2} />
            {t('shell.newChapter')}
          </button>
          <button className="binder-mobile-add" onClick={() => useShellStore.getState().openExport()}>
            <FileDown size={15} strokeWidth={2} />
            {t('shell.view.export')}
          </button>
        </div>
      )}
  </>
}
