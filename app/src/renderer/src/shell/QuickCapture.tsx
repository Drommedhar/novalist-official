import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'

/**
 * Jot something down without deciding where it belongs. The note lands in the
 * Research inbox; filing it properly is a later, separate act. Deliberately one
 * textarea and two keys: Ctrl+Enter saves, Escape cancels.
 *
 * With no project open it goes to the scratchpad instead, which lives beside
 * the settings rather than inside a project. A thought that arrives before the
 * right project is open is still a thought, and the moment one arrives is
 * exactly the moment somebody is not sitting in front of the project it
 * belongs to.
 */
export function QuickCapture({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const areaRef = useRef<HTMLTextAreaElement>(null)
  const hasProject = useProjectStore((s) => s.projectPath !== null)

  useEffect(() => areaRef.current?.focus(), [])

  const save = async (): Promise<void> => {
    const body = text.trim()
    if (body.length === 0 || saving) return
    setSaving(true)
    setError(null)
    try {
      await rpc.request(hasProject ? 'research/quickCapture' : 'scratchpad/add', [body])
      onClose()
    } catch (err) {
      setSaving(false)
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <div
      className="dialog-overlay palette-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="dialog-card palette-card" role="dialog" aria-label={t('capture.quickTitle')}>
        <div className="dialog-title">{t('capture.quickTitle')}</div>
        <textarea
          ref={areaRef}
          className="dialog-input quick-capture-input"
          rows={5}
          value={text}
          placeholder={t('capture.quickPlaceholder')}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') onClose()
            // Enter alone inserts a newline: captures are often more than one line.
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
              e.preventDefault()
              void save()
            }
          }}
        />
        <div className="dialog-hint">
          {t(hasProject ? 'capture.quickHint' : 'capture.scratchpadHint')}
        </div>
        {error && <div className="wiki-summary-error">{error}</div>}
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={saving || text.trim().length === 0}
            onClick={() => void save()}
          >
            {t('capture.quickSave')}
          </button>
        </div>
      </div>
    </div>
  )
}
