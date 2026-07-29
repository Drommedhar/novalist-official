import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Star, X } from 'lucide-react'
import { rpc } from '../rpc/client'

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
 * Presence used to be inferred entirely from @-mentions in the prose. Those are
 * author-confirmed and never wrong, but they are incomplete: a character who is
 * in the room and says nothing leaves no span behind, and the person a scene is
 * really about is often not the one whose name appears most.
 */
export function SceneCastPicker(props: {
  chapterGuid: string
  sceneId: string
  cast: string[]
  focusEntityId: string | null
  onChange: (cast: string[], focusEntityId: string | null) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [entities, setEntities] = useState<EntityOption[]>([])
  const [query, setQuery] = useState('')

  useEffect(() => {
    void Promise.all(
      TYPES.map(async ({ key }) =>
        (await rpc.request<{ id: string; name: string }[]>('entities/list', [key]).catch(() => []))
          .map((e) => ({ ...e, type: key }))
      )
    ).then((lists) => setEntities(lists.flat()))
  }, [])

  const byId = useMemo(
    () => new Map(entities.map((e) => [e.id, e] as const)),
    [entities]
  )

  const needle = query.trim().toLocaleLowerCase()
  const matches =
    needle.length === 0
      ? []
      : entities
          .filter((e) => !props.cast.includes(e.id))
          .filter((e) => e.name.toLocaleLowerCase().includes(needle))
          .slice(0, 8)

  const commit = (cast: string[], focus: string | null): void => {
    props.onChange(cast, focus)
    void rpc.request('scenes/setCast', [props.chapterGuid, props.sceneId, cast, focus])
  }

  return (
    <div className="scene-cast">
      <div className="scene-cast-chips">
        {props.cast.map((id) => {
          const entity = byId.get(id)
          const isFocus = props.focusEntityId === id
          return (
            <span key={id} className={`scene-cast-chip${isFocus ? ' focus' : ''}`}>
              {/* The star says which one the scene is about, which is rarely
                  the one whose name appears most often in it. */}
              <button
                className="scene-cast-star"
                title={t(isFocus ? 'cast.clearFocus' : 'cast.setFocus')}
                onClick={() => commit(props.cast, isFocus ? null : id)}
              >
                <Star size={11} strokeWidth={2} fill={isFocus ? 'currentColor' : 'none'} />
              </button>
              {/* An entity deleted from the Codex leaves its id behind rather
                  than vanishing silently, so the writer can see and remove it. */}
              <span className="scene-cast-name">{entity?.name ?? id}</span>
              <button
                className="scene-cast-remove"
                title={t('cast.remove')}
                onClick={() =>
                  commit(
                    props.cast.filter((c) => c !== id),
                    isFocus ? null : props.focusEntityId
                  )
                }
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
        placeholder={t('cast.addPlaceholder')}
        onChange={(e) => setQuery(e.target.value)}
      />
      {matches.length > 0 && (
        <div className="scene-cast-matches">
          {matches.map((entity) => (
            <button
              key={entity.id}
              className="scene-cast-match"
              onClick={() => {
                commit([...props.cast, entity.id], props.focusEntityId)
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
      )}
    </div>
  )
}
