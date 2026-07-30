import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, Plus, Trash2, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useSelectionStore } from '../stores/selectionStore'

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
export function CollectionsPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const projectPath = useProjectStore((s) => s.projectPath)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const [collections, setCollections] = useState<CollectionDto[]>([])
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [name, setName] = useState('')

  useEffect(() => {
    void rpc
      .request<CollectionDto[]>('collections/list')
      .then(setCollections)
      .catch(() => setCollections([]))
  }, [projectPath])

  const create = (): void => {
    if (name.trim().length === 0) return
    // Whatever is selected goes straight in. Making a collection and then
    // adding the scenes you already had picked is two steps for one intent.
    void rpc
      .request<CollectionDto[]>('collections/create', [name, selectedIds])
      .then(setCollections)
    setName('')
  }

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
              onClick={() =>
                setCollapsed((c) => ({ ...c, [collection.id]: !c[collection.id] }))
              }
            >
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${collapsed[collection.id] ? '' : ' open'}`}
              />
            </button>
            <span className="binder-chapter-title">{collection.name}</span>
            <span className="binder-pin-chapter">{collection.scenes.length}</span>
            {selectedIds.length > 0 && (
              <button
                className="binder-row-action"
                aria-label={t('collections.addSelected')}
                title={t('collections.addSelected')}
                onClick={() =>
                  void rpc
                    .request<CollectionDto[]>('collections/add', [collection.id, selectedIds])
                    .then(setCollections)
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
                void rpc
                  .request<CollectionDto[]>('collections/delete', [collection.id])
                  .then(setCollections)
              }
            >
              <Trash2 size={14} strokeWidth={2} />
            </button>
          </div>
          {!collapsed[collection.id] &&
            collection.scenes.map((scene) => (
              <div key={scene.sceneId} className="collections-row">
                <button
                  className="binder-scene-row"
                  onClick={() =>
                    void useProjectStore.getState().openScene(scene.chapterGuid, scene.sceneId)
                  }
                >
                  <span className="binder-scene-title">{scene.title}</span>
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('collections.remove')}
                  title={t('collections.remove')}
                  onClick={() =>
                    void rpc
                      .request<CollectionDto[]>('collections/remove', [
                        collection.id,
                        scene.sceneId
                      ])
                      .then(setCollections)
                  }
                >
                  <X size={13} strokeWidth={2} />
                </button>
              </div>
            ))}
          {!collapsed[collection.id] && collection.scenes.length === 0 && (
            <div className="binder-placeholder">{t('collections.groupEmpty')}</div>
          )}
        </div>
      ))}
    </div>
  )
}
