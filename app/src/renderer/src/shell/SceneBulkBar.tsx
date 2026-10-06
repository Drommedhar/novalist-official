import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Archive, BookOpen, CalendarClock, Tag, Trash2, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto, type SceneStructureMethod } from '../stores/projectStore'
import { useSelectionStore } from '../stores/selectionStore'
import { useManuscriptStore } from '../stores/manuscriptStore'
import { useShellStore } from '../stores/shellStore'
import { ConfirmDialog } from './ConfirmDialog'
import { InputDialog } from './InputDialog'
import { ShiftDatesDialog } from './ShiftDatesDialog'
import { MotionPresence } from './MotionPresence'
import './scene-bulk-bar.css'

interface BulkResult {
  count: number
  state: ProjectStateDto
}

type Pending = 'delete' | 'archive' | 'tags' | 'shift' | 'move' | null

/**
 * The bar that appears while more than one scene is selected.
 *
 * Everything here is a single round trip that comes back with the new project
 * state, so a bulk change cannot leave the binder showing a stale half of it.
 */
export function SceneBulkBar(): React.JSX.Element {
  const { t } = useTranslation()
  const selected = useSelectionStore((s) => s.sceneIds)
  const clear = useSelectionStore((s) => s.clear)
  const chapters = useProjectStore((s) => s.chapters)
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
  const [pending, setPending] = useState<Pending>(null)
  const active = selected.length >= 2

  const apply = async (method: 'sceneBulk/setTags' | SceneStructureMethod, args: unknown[]): Promise<void> => {
    if (method !== 'sceneBulk/setTags') {
      await useProjectStore.getState().mutateSceneStructure(method, args)
      clear()
      return
    }
    const result = await rpc.request<BulkResult>(method, args)
    useProjectStore.getState().applyState(result.state)
    clear()
  }

  const addTags = (value: string): void => {
    setPending(null)
    const tags = value
      .split(',')
      .map((tag) => tag.trim())
      .filter((tag) => tag.length > 0)
    if (tags.length === 0) return
    void apply('sceneBulk/setTags', [selected, tags, false])
  }

  const moveTo = (chapterGuid: string): void => {
    if (!chapterGuid) return
    const target = chapters.find((c) => c.guid === chapterGuid)
    // Appended, so a bulk move never silently reorders what is already there.
    void apply('sceneBulk/moveToChapter', [selected, chapterGuid, target?.scenes.length ?? 0])
  }

  return (
    <>
      <MotionPresence disabled={suspendMotion}>{active && <div className="scene-bulk-bar">
        <span className="scene-bulk-count">{t('bulk.selected', { count: selected.length })}</span>

        <select
          className="inspector-input scene-bulk-move"
          value=""
          onChange={(e) => moveTo(e.target.value)}
          title={t('bulk.moveToChapter')}
        >
          <option value="">{t('bulk.moveToChapter')}</option>
          {chapters.map((chapter) => (
            <option key={chapter.guid} value={chapter.guid}>
              {chapter.title}
            </option>
          ))}
        </select>

        {/* Reading a chosen run as prose is the point of picking it. */}
        <button
          className="dialog-button"
          onClick={() => {
            void useManuscriptStore.getState().compose(selected)
            useShellStore.getState().setMainView('manuscript')
          }}
        >
          <BookOpen size={14} /> {t('bulk.readAsOne')}
        </button>
        <button className="dialog-button" onClick={() => setPending('tags')}>
          <Tag size={14} /> {t('bulk.addTags')}
        </button>
        <button className="dialog-button" onClick={() => setPending('shift')}>
          <CalendarClock size={14} /> {t('bulk.shiftDates')}
        </button>
        <button className="dialog-button" onClick={() => setPending('archive')}>
          <Archive size={14} /> {t('explorer.contextArchive')}
        </button>
        <button className="dialog-button danger" onClick={() => setPending('delete')}>
          <Trash2 size={14} /> {t('explorer.contextDelete')}
        </button>
        <button className="dialog-button" onClick={clear} title={t('bulk.clear')}>
          <X size={14} />
        </button>
      </div>}</MotionPresence>

      <MotionPresence disabled={suspendMotion}>{active && pending === 'tags' && (
        <InputDialog
          title={t('bulk.addTagsPrompt', { count: selected.length })}
          placeholder={t('bulk.addTagsPlaceholder')}
          onCancel={() => setPending(null)}
          onSubmit={addTags}
        />
      )}</MotionPresence>

      <MotionPresence disabled={suspendMotion}>{active && pending === 'archive' && (
        <ConfirmDialog
          title={t('explorer.contextArchive')}
          message={t('bulk.confirmArchive', { count: selected.length })}
          confirmLabel={t('explorer.contextArchive')}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void apply('sceneBulk/archive', [selected])
          }}
        />
      )}</MotionPresence>

      <MotionPresence disabled={suspendMotion}>{active && pending === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('bulk.confirmDelete', { count: selected.length })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void apply('sceneBulk/delete', [selected])
          }}
        />
      )}</MotionPresence>

      <MotionPresence disabled={suspendMotion}>{active && pending === 'shift' && (
        <ShiftDatesDialog
          sceneIds={selected}
          onClose={() => setPending(null)}
          onApplied={(state) => {
            setPending(null)
            useProjectStore.getState().applyState(state)
            clear()
          }}
        />
      )}</MotionPresence>
    </>
  )
}
