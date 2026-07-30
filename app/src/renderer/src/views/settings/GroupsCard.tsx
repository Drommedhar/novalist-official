import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface EntityGroupDto {
  id: string
  name: string
  color: string
  description: string
  memberCount: number
}

/**
 * Factions, houses, crews and families.
 *
 * The group was a bare string on each Codex entry: it could say a house and a
 * ship both belong to the Ravens and nothing else — no colour, no description,
 * no count, and no rename, so correcting "the Ravens" to "House Raven" meant
 * opening every entry that said the first thing.
 *
 * Renaming here rewrites every entry in the group, across every type.
 */
export function GroupsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [groups, setGroups] = useState<EntityGroupDto[]>([])
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void rpc.request<EntityGroupDto[]>('groups/list').then(setGroups).catch(() => setGroups([]))
  }, [])

  const edit = (index: number, patch: Partial<EntityGroupDto>): void => {
    setDirty(true)
    setGroups(groups.map((g, i) => (i === index ? { ...g, ...patch } : g)))
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('groups.intro')}</div>

      {groups.map((group, index) => (
        <div key={group.id || index} className="match-row">
          <input
            className="inspector-input"
            value={group.name}
            placeholder={t('groups.namePlaceholder')}
            onChange={(e) => edit(index, { name: e.target.value })}
            onBlur={() => {
              // A rename has to reach the Codex, so it is its own operation
              // rather than part of the bulk save.
              if (!group.id) return
              void rpc
                .request<EntityGroupDto[]>('groups/rename', [group.id, group.name])
                .then(setGroups)
            }}
          />
          <input
            className="dialog-input settings-color"
            type="color"
            aria-label={t('groups.colour')}
            value={group.color}
            onChange={(e) => edit(index, { color: e.target.value })}
          />
          <input
            className="inspector-input"
            value={group.description}
            placeholder={t('groups.descriptionPlaceholder')}
            onChange={(e) => edit(index, { description: e.target.value })}
          />
          <span className="settings-hint">
            {t('groups.members', { count: group.memberCount })}
          </span>
          <button
            className="binder-row-action"
            aria-label={t('groups.delete')}
            title={t('groups.delete')}
            onClick={() =>
              void rpc.request<EntityGroupDto[]>('groups/delete', [group.id, true]).then(setGroups)
            }
          >
            <Trash2 size={15} strokeWidth={2} />
          </button>
        </div>
      ))}

      <div className="match-row">
        <button
          className="btn-secondary"
          onClick={() => {
            setDirty(true)
            setGroups([
              ...groups,
              { id: '', name: '', color: '#8b8b8b', description: '', memberCount: 0 }
            ])
          }}
        >
          <Plus size={15} strokeWidth={2} />
          {t('groups.add')}
        </button>
        {/* Without this an existing project starts on an empty list, which is
            no use to whoever already has the most groups. */}
        <button
          className="btn-secondary"
          onClick={() => void rpc.request<EntityGroupDto[]>('groups/harvest').then(setGroups)}
        >
          {t('groups.harvest')}
        </button>
        <button
          className="btn-primary"
          disabled={!dirty}
          onClick={() =>
            void rpc.request<EntityGroupDto[]>('groups/save', [groups]).then((saved) => {
              setGroups(saved)
              setDirty(false)
            })
          }
        >
          {t('groups.save')}
        </button>
      </div>
    </div>
  )
}
