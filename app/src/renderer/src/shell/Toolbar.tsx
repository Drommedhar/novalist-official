import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  GitCompare,
  History,
  Menu,
  PanelBottom,
  PanelLeft,
  PanelRight,
  Plus,
  Search,
  Trash2
} from 'lucide-react'
import { useShellStore } from '../stores/shellStore'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { InputDialog } from './InputDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { ChapterDialog } from './ChapterDialog'
import { SceneDialog } from './SceneDialog'
import { StartMenuOverlay } from './StartMenuOverlay'
import { SnapshotsDialog } from './SnapshotsDialog'
import { DraftCompareDialog } from './DraftCompareDialog'

type PendingDialog = 'chapter' | 'scene' | 'book' | 'draft' | 'renameProject' | null

export function Toolbar(): React.JSX.Element {
  const { t } = useTranslation()
  const toggleBinder = useShellStore((s) => s.toggleBinder)
  const toggleInspector = useShellStore((s) => s.toggleInspector)
  const toggleNotesDock = useShellStore((s) => s.toggleNotesDock)
  const notesDockVisible = useShellStore((s) => s.notesDockVisible)
  const projectName = useProjectStore((s) => s.projectName)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  const drafts = useProjectStore((s) => s.drafts)
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const [dialog, setDialog] = useState<PendingDialog>(null)
  const [startMenuOpen, setStartMenuOpen] = useState(false)
  const [snapshotsOpen, setSnapshotsOpen] = useState(false)
  const [compareDrafts, setCompareDrafts] = useState(false)
  const [deleteDraftTarget, setDeleteDraftTarget] = useState<{ id: string; name: string } | null>(
    null
  )
  const isMac = window.novalist.platform === 'darwin'
  // Desktop Windows/Linux hide the native title bar and overlay the system
  // window controls on this strip, so it needs room for them at its right edge.
  const hasControlsOverlay = !isMac && !window.novalist.isMobile

  const targetChapter = openChapterGuid ?? chapters[chapters.length - 1]?.guid ?? null
  const activeDraft = drafts.find((d) => d.isActive) ?? null

  return (
    <header
      className={`toolbar${isMac ? ' toolbar-mac' : ''}${
        hasControlsOverlay ? ' toolbar-overlay' : ''
      }`}
    >
      {isLoaded && (
        <button className="toolbar-button" title={t('shell.menu')} onClick={() => setStartMenuOpen(true)}>
          <Menu size={16} strokeWidth={1.75} />
        </button>
      )}
      <button
        className="toolbar-book"
        title={t('explorer.contextRename')}
        onDoubleClick={() => projectName && setDialog('renameProject')}
      >
        {projectName ?? 'Novalist'}
      </button>
      {books.length > 0 && (
        <select
          className="toolbar-select"
          value={activeBookId ?? ''}
          onChange={(e) => {
            if (e.target.value === '__new__') setDialog('book')
            else void useProjectStore.getState().switchBook(e.target.value)
          }}
        >
          {books.map((book) => (
            <option key={book.id} value={book.id}>
              {book.name}
            </option>
          ))}
          <option value="__new__">{t('book.addBook')}</option>
        </select>
      )}
      {drafts.length > 0 && (
        <div className="toolbar-draft">
          <select
            className="toolbar-select"
            value={activeDraft?.id ?? ''}
            onChange={(e) => {
              if (e.target.value === '__new__') setDialog('draft')
              else void useProjectStore.getState().switchDraft(e.target.value)
            }}
          >
            {drafts.map((draft) => (
              <option key={draft.id} value={draft.id}>
                {draft.name}
              </option>
            ))}
            <option value="__new__">{t('draft.add')}</option>
          </select>
          <button
            className="toolbar-button"
            title={t('draftCompare.title')}
            disabled={drafts.length < 2}
            onClick={() => setCompareDrafts(true)}
          >
            <GitCompare size={14} strokeWidth={1.75} />
          </button>
          <button
            className="toolbar-button"
            title={t('draft.deleteTitle')}
            disabled={drafts.length <= 1 || !activeDraft}
            onClick={() =>
              activeDraft && setDeleteDraftTarget({ id: activeDraft.id, name: activeDraft.name })
            }
          >
            <Trash2 size={14} strokeWidth={1.75} />
          </button>
        </div>
      )}
      {/* Everything past the wordmark acts on an open project - adding a chapter
          or scene, searching it, or toggling panels that the welcome screen does
          not have. The spacer stays either way so the strip keeps its drag
          region and its room for the window controls. */}
      {isLoaded && (
        <>
          <button className="toolbar-button toolbar-action" onClick={() => setDialog('chapter')}>
            <Plus size={14} strokeWidth={2} />
            {t('shell.newChapter')}
          </button>
          <button
            className="toolbar-button toolbar-action"
            disabled={targetChapter === null}
            onClick={() => setDialog('scene')}
          >
            <Plus size={14} strokeWidth={2} />
            {t('shell.newScene')}
          </button>
        </>
      )}
      <div className="toolbar-spacer" />
      {isLoaded && (
        <>
          <button
            className="toolbar-button"
            title={t('findReplace.title')}
            onClick={() => useShellStore.getState().setFindReplaceOpen(true)}
          >
            <Search size={15} strokeWidth={1.75} />
          </button>
          <button
            className="toolbar-button"
            title={t('shell.snapshots')}
            disabled={!openChapterGuid || !openSceneId}
            onClick={() => setSnapshotsOpen(true)}
          >
            <History size={15} strokeWidth={1.75} />
          </button>
          <button className="toolbar-button" title={t('shell.toggleBinder')} onClick={toggleBinder}>
            <PanelLeft size={16} strokeWidth={1.75} />
          </button>
          <button
            className={`toolbar-button${notesDockVisible ? ' active' : ''}`}
            title={t('shell.toggleSceneNotes')}
            onClick={toggleNotesDock}
          >
            <PanelBottom size={16} strokeWidth={1.75} />
          </button>
          <button
            className="toolbar-button"
            title={t('shell.toggleInspector')}
            onClick={toggleInspector}
          >
            <PanelRight size={16} strokeWidth={1.75} />
          </button>
        </>
      )}
      {startMenuOpen && <StartMenuOverlay onClose={() => setStartMenuOpen(false)} />}
      {snapshotsOpen && openChapterGuid && openSceneId && (
        <SnapshotsDialog
          chapterGuid={openChapterGuid}
          sceneId={openSceneId}
          onClose={() => setSnapshotsOpen(false)}
        />
      )}
      {dialog === 'renameProject' && (
        <InputDialog
          title={t('explorer.contextRename')}
          placeholder={projectName ?? ''}
          onCancel={() => setDialog(null)}
          onSubmit={(name) => {
            setDialog(null)
            void (async () => {
              const state = await rpc.request<import('../stores/projectStore').ProjectStateDto>(
                'project/rename',
                [name]
              )
              useProjectStore.getState().applyState(state)
            })()
          }}
        />
      )}
      {dialog === 'book' && (
        <InputDialog
          title={t('book.addBookTitle')}
          onCancel={() => setDialog(null)}
          onSubmit={(name) => {
            setDialog(null)
            void useProjectStore.getState().createBook(name)
          }}
        />
      )}
      {dialog === 'draft' && (
        <InputDialog
          title={t('draft.newTitle')}
          onCancel={() => setDialog(null)}
          onSubmit={(name) => {
            setDialog(null)
            void useProjectStore.getState().createDraft(name)
          }}
        />
      )}
      {dialog === 'chapter' && <ChapterDialog onClose={() => setDialog(null)} />}
      {dialog === 'scene' && targetChapter !== null && (
        <SceneDialog defaultChapterGuid={targetChapter} onClose={() => setDialog(null)} />
      )}
      {deleteDraftTarget && (
        <ConfirmDialog
          title={t('draft.deleteTitle')}
          message={t('draft.deleteMessage').replace('{0}', deleteDraftTarget.name)}
          onCancel={() => setDeleteDraftTarget(null)}
          onConfirm={() => {
            const id = deleteDraftTarget.id
            setDeleteDraftTarget(null)
            void useProjectStore.getState().deleteDraft(id)
          }}
        />
      )}
      {compareDrafts && <DraftCompareDialog onClose={() => setCompareDrafts(false)} />}
    </header>
  )
}
