import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Star, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { MotionPresence } from './MotionPresence'
import type { SharedEntityFields } from '../collaboration/FocusedField'
import { metadataBinding, SharedMetadataField } from '../collaboration/SharedMetadataField'
import { writeSharedMetadata } from '../collaboration/metadataCommands'
import { useSharedProjectRevision } from '../collaboration/sharedProjectNotifications'
import { flushPendingWrites } from '../stores/pendingWrites'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

interface EntityOption {
  id: string
  name: string
  type: string
}

const TYPES = [
  { key: 'character', labelKey: 'codexHub.characters' },
  { key: 'location', labelKey: 'codexHub.locations' },
  { key: 'item', labelKey: 'codexHub.items' },
  { key: 'lore', labelKey: 'codexHub.lore' }
]

/**
 * Who and what is in this scene, said outright.
 *
 * Explicit presence complements prose mentions: a silent character can still
 * belong to the scene, and its focus need not be its most-mentioned entity.
 */
export function SceneCastPicker(props: {
  chapterGuid: string
  sceneId: string
  cast: string[]
  focusEntityId: string | null
  sharedFields?: SharedEntityFields
  onChange: (cast: string[], focusEntityId: string | null) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [entities, setEntities] = useState<EntityOption[]>([])
  const [query, setQuery] = useState('')
  const revision = useSharedProjectRevision()
  const focusBinding = metadataBinding(props, 'focusEntityId')

  useEffect(() => {
    void Promise.all(
      TYPES.map(async ({ key }) =>
        (await rpc.request<{ id: string; name: string }[]>('entities/list', [key]).catch(() => []))
          .map((e) => ({ ...e, type: key }))
      )
    ).then((lists) => setEntities(lists.flat()))
  }, [revision])

  const byId = useMemo(
    () => new Map(entities.map((e) => [e.id, e] as const)),
    [entities]
  )

  const needle = query.trim().toLocaleLowerCase()
  const matches =
    needle.length === 0
      ? []
      : entities
          .filter((e) => e.name.toLocaleLowerCase().includes(needle))
          .slice(0, 8)

  const commit = (cast: string[], focus: string | null): void => {
    props.onChange(cast, focus)
    void rpc.request('scenes/setCast', [props.chapterGuid, props.sceneId, cast, focus])
  }

  return <SharedMetadataField<string[] | null> binding={metadataBinding(props, 'cast')} value={props.cast}
    onChange={cast => commit(cast ?? [], props.focusEntityId && cast?.includes(props.focusEntityId) ? props.focusEntityId : null)}>{field => (
    <div className="scene-cast">
      <div className="scene-cast-chips">
        {(field.value ?? []).map((id) => {
          const entity = byId.get(id)
          const isFocus = props.focusEntityId === id
          return (
            <span key={id} className={`scene-cast-chip${isFocus ? ' focus' : ''}`}>
              {/* The star says which one the scene is about, which is rarely
                  the one whose name appears most often in it. */}
              <button
                className="scene-cast-star"
                title={t(isFocus ? 'cast.clearFocus' : 'cast.setFocus')}
                onClick={() => {
                  if (focusBinding) void flushPendingWrites()
                    .then(() => writeSharedMetadata<string | null>(focusBinding, current => current === id ? null : id))
                    .catch(error => useHostBridgeStore.getState().pushToast(String(error)))
                  else commit(field.value ?? [], isFocus ? null : id)
                }}
              >
                <Star size={11} strokeWidth={2} fill={isFocus ? 'currentColor' : 'none'} />
              </button>
              {/* An entity deleted from the Codex leaves its id behind rather
                  than vanishing silently, so the writer can see and remove it. */}
              <span className="scene-cast-name">{entity?.name ?? id}</span>
              <button
                className="scene-cast-remove"
                title={t('cast.remove')}
                aria-disabled={!field.editable}
                onClick={() => field.change(cast => (cast ?? []).filter(member => member !== id))}
              >
                <X size={11} strokeWidth={2} />
              </button>
            </span>
          )
        })}
      </div>

      <input
        className="inspector-input"
        value={query}
        readOnly={!field.editable}
        placeholder={t('cast.addPlaceholder')}
        onChange={(e) => setQuery(e.target.value)}
      />
      <MotionPresence collapse>{matches.length > 0 && (
        <div className="scene-cast-matches">
          {matches.filter(entity => !field.value?.includes(entity.id)).map((entity) => (
            <button
              key={entity.id}
              className="scene-cast-match"
              aria-disabled={!field.editable}
              onClick={() => {
                if (!field.editable) return
                field.change(cast => [...(cast ?? []), entity.id])
                setQuery('')
              }}
            >
              {entity.name}
              <span className="scene-cast-type">
                {t(TYPES.find((ty) => ty.key === entity.type)!.labelKey)}
              </span>
            </button>
          ))}
        </div>
      )}</MotionPresence>
    </div>
  )}</SharedMetadataField>
}
