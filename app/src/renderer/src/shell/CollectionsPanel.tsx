import { useEffect, useState } from 'react'
import { MotionPresence } from './MotionPresence'
import { useTranslation } from 'react-i18next'
import { ArrowDown, ArrowUp, Check, ChevronRight, Pencil, Plus, Trash2, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useBookScope, useProjectStore } from '../stores/projectStore'
import { loadBookScoped } from '../stores/bookScopedLoad'
import { useSelectionStore } from '../stores/selectionStore'
import { openBinderScene } from './binderNavigation'
import { useWorkspaceDialogGuard } from './useWorkspaceDialogGuard'

interface CollectionSceneDto {
  sceneId: string
  chapterGuid: string
  title: string
}

interface CollectionDto {
  id: string
  name: string
  scenes: CollectionSceneDto[]
}

/**
 * Hand-curated scene sets.
 *
 * A saved list answers "which scenes match this query" and recomputes every time
 * it is opened. A collection answers a question no filter can: the eight scenes
 * to fix before Tuesday, the run being read to a writing group, the ones a beta
 * reader stumbled on. Nothing they have in common is expressible as a query -
 * which is exactly why they had to be gathered by hand.
 *
 * The order inside a collection is the writer's, not reading order. A revision
 * run is often deliberately out of sequence, and re-sorting it would throw away
 * the only thing the writer said about the set.
 */
function useCollections() {
  const { t } = useTranslation()
  const bookScope = useBookScope()
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const [collections, setCollections] = useState<CollectionDto[]>([])
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [name, setName] = useState('')
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const [renameValue, setRenameValue] = useState('')
  useWorkspaceDialogGuard(name.trim().length > 0 || renamingId !== null)

  useEffect(() => {
    let active = true
    void loadBookScoped(useProjectStore.getState,
      () => rpc.request<CollectionDto[]>('collections/list'),
      (items) => { if (active) setCollections(items) })
      .catch(() => { if (active) setCollections([]) })
    // Collections belong to the active book, so switching books has to refetch.
    return () => { active = false }
  }, [bookScope, workspaceBusy])

  const update = (method: string, params: unknown[]): Promise<void> =>
    loadBookScoped(useProjectStore.getState,
      () => rpc.request<CollectionDto[]>(method, params), setCollections)

  const create = (): void => {
    if (name.trim().length === 0) return
    // Whatever is selected goes straight in. Making a collection and then
    // adding the scenes you already had picked is two steps for one intent.
    void update('collections/create', [name, selectedIds])
    setName('')
  }

  const rename = (): void => {
    if (renamingId === null || renameValue.trim().length === 0) return
    void update('collections/rename', [renamingId, renameValue])
    setRenamingId(null)
    setRenameValue('')
  }

  return { t, name, setName, create, selectedIds, collections, setCollapsed, collapsed, renamingId, renameValue, setRenameValue, rename, setRenamingId, update }
}

export function CollectionsPanel(): React.JSX.Element {
  const { t, name, setName, create, selectedIds, collections, setCollapsed, collapsed, renamingId, renameValue, setRenameValue, rename, setRenamingId, update } = useCollections()

  return (
    <div className="collections-panel">
      <div className="collections-new">
        <input
          className="inspector-input"
          placeholder={t('collections.namePlaceholder')}
          value={name}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && create()}
        />
        <button
          className="binder-row-action"
          aria-label={t('collections.create')}
          title={t('collections.create')}
          disabled={name.trim().length === 0}
          onClick={create}
        >
          <Plus size={15} strokeWidth={2} />
        </button>
      </div>
      {selectedIds.length > 0 && (
        <div className="settings-hint collections-hint">
          {t('collections.willInclude', { count: selectedIds.length })}
        </div>
      )}

      {collections.length === 0 && (
        <div className="binder-placeholder">{t('collections.empty')}</div>
      )}

      {collections.map((collection) => (
        <div key={collection.id} className="collections-group">
          <div className="collections-head">
            <button
              className="binder-expand"
              aria-label={collection.name}
              onClick={() => setCollapsed((c) => ({ ...c, [collection.id]: !c[collection.id] }))}
            >
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${collapsed[collection.id] ? '' : ' open'}`}
              />
            </button>
            {renamingId === collection.id ? (
              <input
                className="inspector-input collections-rename"
                aria-label={t('collections.rename')}
                value={renameValue}
                autoFocus
                onChange={(e) => setRenameValue(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') rename()
                  if (e.key === 'Escape') setRenamingId(null)
                }}
              />
            ) : (
              <span className="binder-chapter-title">{collection.name}</span>
            )}
            <span className="binder-pin-chapter">{collection.scenes.length}</span>
            {renamingId === collection.id ? (
              <button
                className="binder-row-action"
                aria-label={t('collections.saveName')}
                title={t('collections.saveName')}
                disabled={renameValue.trim().length === 0}
                onClick={rename}
              >
                <Check size={14} strokeWidth={2} />
              </button>
            ) : (
              <button
                className="binder-row-action"
                aria-label={t('collections.rename')}
                title={t('collections.rename')}
                onClick={() => {
                  setRenamingId(collection.id)
                  setRenameValue(collection.name)
                }}
              >
                <Pencil size={14} strokeWidth={2} />
              </button>
            )}
            {selectedIds.length > 0 && (
              <button
                className="binder-row-action"
                aria-label={t('collections.addSelected')}
                title={t('collections.addSelected')}
                onClick={() =>
                  void update('collections/add', [collection.id, selectedIds])
                }
              >
                <Plus size={14} strokeWidth={2} />
              </button>
            )}
            <button
              className="binder-row-action"
              aria-label={t('collections.delete')}
              title={t('collections.delete')}
              onClick={() =>
                void update('collections/delete', [collection.id])
              }
            >
              <Trash2 size={14} strokeWidth={2} />
            </button>
          </div>
          <MotionPresence collapse>
          {!collapsed[collection.id] && <div>
            {collection.scenes.map((scene, index) => (
              <div key={scene.sceneId} className="collections-row">
                <button
                  className="binder-scene-row"
                  onClick={() => openBinderScene(scene.chapterGuid, scene.sceneId)}
                >
                  <span className="binder-scene-title">{scene.title}</span>
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('collections.moveUp')}
                  title={t('collections.moveUp')}
                  disabled={index === 0}
                  onClick={() =>
                    void update('collections/move', [
                        collection.id,
                        scene.sceneId,
                        index - 1
                      ])
                  }
                >
                  <ArrowUp size={13} strokeWidth={2} />
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('collections.moveDown')}
                  title={t('collections.moveDown')}
                  disabled={index === collection.scenes.length - 1}
                  onClick={() =>
                    void update('collections/move', [
                        collection.id,
                        scene.sceneId,
                        index + 1
                      ])
                  }
                >
                  <ArrowDown size={13} strokeWidth={2} />
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('collections.remove')}
                  title={t('collections.remove')}
                  onClick={() =>
                    void update('collections/remove', [
                        collection.id,
                        scene.sceneId
                      ])
                  }
                >
                  <X size={13} strokeWidth={2} />
                </button>
              </div>
            ))}
          {collection.scenes.length === 0 && (
            <div className="binder-placeholder">{t('collections.groupEmpty')}</div>
          )}
          </div>}
          </MotionPresence>
        </div>
      ))}
    </div>
  )
}
