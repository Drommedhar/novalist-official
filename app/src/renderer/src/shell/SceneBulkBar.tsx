import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Archive, CalendarClock, Tag, Trash2, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useSelectionStore } from '../stores/selectionStore'
import { ConfirmDialog } from './ConfirmDialog'
import { InputDialog } from './InputDialog'
import { ShiftDatesDialog } from './ShiftDatesDialog'
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
export function SceneBulkBar(): React.JSX.Element | null {
  const { t } = useTranslation()
  const selected = useSelectionStore((s) => s.sceneIds)
  const clear = useSelectionStore((s) => s.clear)
  const chapters = useProjectStore((s) => s.chapters)
  const [pending, setPending] = useState<Pending>(null)

  // One selected scene is just an open scene; the bar is for acting on several.
  if (selected.length < 2) return null

  const apply = async (method: string, args: unknown[]): Promise<void> => {
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
      <div className="scene-bulk-bar">
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
      </div>

      {pending === 'tags' && (
        <InputDialog
          title={t('bulk.addTagsPrompt', { count: selected.length })}
          placeholder={t('bulk.addTagsPlaceholder')}
          onCancel={() => setPending(null)}
          onSubmit={addTags}
        />
      )}

      {pending === 'archive' && (
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
      )}

      {pending === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('bulk.confirmDelete', { count: selected.length })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void apply('sceneBulk/delete', [selected])
          }}
        />
      )}

      {pending === 'shift' && (
        <ShiftDatesDialog
          sceneIds={selected}
          onClose={() => setPending(null)}
          onApplied={(state) => {
            setPending(null)
            useProjectStore.getState().applyState(state)
            clear()
          }}
        />
      )}
    </>
  )
}
