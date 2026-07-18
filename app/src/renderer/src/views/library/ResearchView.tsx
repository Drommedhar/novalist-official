import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'

interface ResearchItemDto {
  id: string
  title: string
  type: string
  content: string
  tags: string[]
}

const TYPES = ['Note', 'Link', 'File', 'Image']

export function ResearchView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [items, setItems] = useState<ResearchItemDto[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (mainView !== 'research') return
    void rpc.request<ResearchItemDto[]>('research/list').then(setItems)
  }, [mainView])

  const selected = items.find((i) => i.id === selectedId) ?? null

  const save = async (item: ResearchItemDto): Promise<void> => {
    const updated = await rpc.request<ResearchItemDto[]>('research/save', [
      item.id,
      item.title,
      item.type,
      item.content,
      item.tags
    ])
    setItems(updated)
  }

  const patchSelected = (patch: Partial<ResearchItemDto>): void => {
    if (!selected) return
    setItems(items.map((i) => (i.id === selected.id ? { ...i, ...patch } : i)))
  }

  return (
    <div className="codex">
      <div className="codex-body">
        <div className="codex-list">
          <button
            className="binder-rail-item"
            onClick={() =>
              void rpc
                .request<ResearchItemDto[]>('research/save', [
                  null,
                  t('research.titleWatermark'),
                  'Note',
                  '',
                  []
                ])
                .then((updated) => {
                  setItems(updated)
                  setSelectedId(updated[updated.length - 1]?.id ?? null)
                })
            }
          >
            <Plus size={14} strokeWidth={2} />
            {t('research.addNote')}
          </button>
          {items.map((item) => (
            <button
              key={item.id}
              className={`codex-row${selectedId === item.id ? ' active' : ''}`}
              onClick={() => setSelectedId(item.id)}
            >
              <span className="codex-row-text">
                <span className="codex-row-name">{item.title}</span>
                <span className="codex-row-detail">{item.type}</span>
              </span>
            </button>
          ))}
          {items.length === 0 && <p className="codex-empty">{t('research.empty')}</p>}
        </div>
        <div className="codex-detail">
          {selected ? (
            <div className="research-editor">
              <div className="codex-detail-actions">
                <button className="dialog-button danger" onClick={() => setConfirmDelete(true)}>
                  <Trash2 size={13} strokeWidth={2} /> {t('explorer.contextDelete')}
                </button>
              </div>
              <input
                className="dialog-input"
                value={selected.title}
                onChange={(e) => patchSelected({ title: e.target.value })}
                onBlur={() => void save(selected)}
              />
              <select
                className="dialog-input"
                value={selected.type}
                onChange={(e) => {
                  patchSelected({ type: e.target.value })
                  void save({ ...selected, type: e.target.value })
                }}
              >
                {TYPES.map((type) => (
                  <option key={type} value={type}>
                    {t(`research.type${type}`)}
                  </option>
                ))}
              </select>
              <textarea
                className="inspector-textarea research-content"
                rows={16}
                value={selected.content}
                onChange={(e) => patchSelected({ content: e.target.value })}
                onBlur={() => void save(selected)}
              />
              <input
                className="dialog-input"
                placeholder={t('research.tags')}
                value={selected.tags.join(', ')}
                onChange={(e) =>
                  patchSelected({ tags: e.target.value.split(',').map((tag) => tag.trim()) })
                }
                onBlur={() => void save(selected)}
              />
            </div>
          ) : (
            <p className="codex-empty">{t('research.empty')}</p>
          )}
        </div>
      </div>
      {confirmDelete && selected && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={selected.title}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => {
            setConfirmDelete(false)
            void rpc.request<ResearchItemDto[]>('research/delete', [selected.id]).then((updated) => {
              setItems(updated)
              setSelectedId(null)
            })
          }}
        />
      )}
    </div>
  )
}
