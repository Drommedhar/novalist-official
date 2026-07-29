import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Merge, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { InputDialog } from '../../shell/InputDialog'

interface TagUsage {
  name: string
  color: string
  scenes: number
  entities: number
  research: number
  total: number
}

/**
 * The project's tags, in one place.
 *
 * Scenes, Codex entries and research notes each kept their own list, none of
 * them coloured and none of them countable, so a tag was three unrelated words
 * that happened to be spelled alike. This is the vocabulary they all draw
 * from: what exists, what carries it, and the two operations that fix a list
 * that has drifted - rename and merge.
 */
export function TagsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [tags, setTags] = useState<TagUsage[]>([])
  const [renaming, setRenaming] = useState<TagUsage | null>(null)

  useEffect(() => {
    void rpc.request<TagUsage[]>('tags/list').then(setTags).catch(() => setTags([]))
  }, [])

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('tags.intro')}</div>

      {tags.length === 0 && <p className="codex-empty">{t('tags.empty')}</p>}

      {tags.map((tag) => (
        <div key={tag.name} className="match-row">
          <input
            className="dialog-input settings-color"
            type="color"
            aria-label={t('tags.colour')}
            value={tag.color || '#7f7f7f'}
            onChange={(e) =>
              void rpc
                .request<TagUsage[]>('tags/setColor', [tag.name, e.target.value])
                .then(setTags)
            }
          />
          <button className="tag-name" onClick={() => setRenaming(tag)}>
            {tag.name}
          </button>
          <span className="settings-hint">
            {t('tags.counts', {
              scenes: tag.scenes,
              entities: tag.entities,
              research: tag.research
            })}
          </span>
          <button
            className="match-remove"
            title={t('tags.rename')}
            aria-label={`${t('tags.rename')}: ${tag.name}`}
            onClick={() => setRenaming(tag)}
          >
            <Merge size={13} strokeWidth={2} />
          </button>
          <button
            className="match-remove"
            title={t('tags.delete')}
            aria-label={`${t('tags.delete')}: ${tag.name}`}
            onClick={() =>
              void rpc.request<TagUsage[]>('tags/delete', [tag.name]).then(setTags)
            }
          >
            <Trash2 size={13} strokeWidth={2} />
          </button>
        </div>
      ))}

      {renaming && (
        <InputDialog
          title={t('tags.renameTitle', { name: renaming.name })}
          placeholder={t('tags.renamePlaceholder')}
          initialValue={renaming.name}
          onCancel={() => setRenaming(null)}
          onSubmit={(value) => {
            const from = renaming.name
            setRenaming(null)
            void rpc.request<TagUsage[]>('tags/rename', [from, value]).then(setTags)
          }}
        />
      )}
    </div>
  )
}
