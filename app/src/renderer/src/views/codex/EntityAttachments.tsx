import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileText, Film, Link2, Music, Paperclip, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'

interface AttachmentDto {
  id: string
  name: string
  kind: 'File' | 'Audio' | 'Video' | 'Document' | 'Link'
  url: string
  note: string
  fullPath: string
}

/** A picture for what the thing is, so a list of ten is scannable. */
function icon(kind: AttachmentDto['kind']): React.JSX.Element {
  const size = 14
  if (kind === 'Audio') return <Music size={size} strokeWidth={2} />
  if (kind === 'Video') return <Film size={size} strokeWidth={2} />
  if (kind === 'Document') return <FileText size={size} strokeWidth={2} />
  if (kind === 'Link') return <Link2 size={size} strokeWidth={2} />
  return <Paperclip size={size} strokeWidth={2} />
}

/**
 * Files kept with a Codex entry.
 *
 * Entries could hold images and nothing else, so a recorded interview with the
 * person a character is based on, or the deed that settles who owns the house,
 * had to be filed as a Research item and linked back — stored and surfaced
 * somewhere other than the entry it is about.
 */
export function EntityAttachments(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const [items, setItems] = useState<AttachmentDto[]>([])
  const [linkUrl, setLinkUrl] = useState('')

  useEffect(() => {
    if (!selectedId) {
      setItems([])
      return
    }
    void rpc
      .request<AttachmentDto[]>('attachments/list', [entityType, selectedId])
      .then(setItems)
      .catch(() => setItems([]))
  }, [entityType, selectedId])

  if (!selectedId) return null

  const addFile = (): void => {
    void window.novalist.pickFile(t('attachments.add'), 'all').then((path) => {
      if (!path) return
      void rpc
        .request<AttachmentDto[]>('attachments/add', [entityType, selectedId, path])
        .then(setItems)
    })
  }

  const addLink = (): void => {
    if (linkUrl.trim().length === 0) return
    void rpc
      .request<AttachmentDto[]>('attachments/addLink', [entityType, selectedId, linkUrl])
      .then((next) => {
        setItems(next)
        setLinkUrl('')
      })
  }

  return (
    <div className="entity-attachments">
      <label className="inspector-label">{t('attachments.title')}</label>

      {items.map((item) => (
        <div key={item.id} className="attachment-row">
          <span className="attachment-icon">{icon(item.kind)}</span>
          <input
            className="attachment-name"
            defaultValue={item.name}
            aria-label={t('attachments.name')}
            onBlur={(e) => {
              if (e.target.value === item.name) return
              void rpc
                .request<AttachmentDto[]>('attachments/update', [
                  entityType,
                  selectedId,
                  item.id,
                  e.target.value
                ])
                .then(setItems)
            }}
          />
          {/* One opener for both: it already sends an http address to the
              browser and anything else to whatever the machine uses for that
              kind. Nothing is previewed here - the entry is about the writing,
              and a media player embedded in it would be in the way. */}
          <button
            className="btn-secondary"
            onClick={() => void window.novalist.openExternal(item.url || item.fullPath)}
          >
            {t('attachments.open')}
          </button>
          <button
            className="binder-row-action"
            aria-label={t('attachments.remove')}
            title={t('attachments.remove')}
            onClick={() =>
              void rpc
                .request<AttachmentDto[]>('attachments/remove', [entityType, selectedId, item.id])
                .then(setItems)
            }
          >
            <X size={14} strokeWidth={2} />
          </button>
        </div>
      ))}

      <div className="attachment-row">
        <button className="btn-secondary" onClick={addFile}>
          {t('attachments.add')}
        </button>
        <input
          className="inspector-input"
          placeholder={t('attachments.linkPlaceholder')}
          value={linkUrl}
          onChange={(e) => setLinkUrl(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && addLink()}
        />
      </div>
    </div>
  )
}
