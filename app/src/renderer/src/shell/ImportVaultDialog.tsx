import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen } from 'lucide-react'
import { rpc } from '../rpc/client'

interface VaultNote {
  relativePath: string
  title: string
  tags: string[]
}

interface VaultScan {
  total: number
  firstFew: VaultNote[]
  tags: string[]
}

/**
 * Brings a folder of ordinary Markdown files in as research notes.
 *
 * Novalist imported one thing: a vault made by the old Obsidian plugin, with
 * its own metadata files. A folder of ordinary notes — which is what a vault is
 * once the plugin is gone, and what every other tool exports — had no way in.
 */
export function ImportVaultDialog({ onClose }: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const [folder, setFolder] = useState('')
  const [scan, setScan] = useState<VaultScan | null>(null)
  const [tag, setTag] = useState('')
  const [imported, setImported] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const browse = async (): Promise<void> => {
    const picked = await window.novalist.pickFolder(t('vaultImport.selectFolder'))
    if (!picked) return
    setError(null)
    setImported(null)
    setFolder(picked)
    setBusy(true)
    try {
      setScan(await rpc.request<VaultScan>('import/scanVault', [picked]))
    } catch (e) {
      setError(String(e))
      setScan(null)
    } finally {
      setBusy(false)
    }
  }

  const run = async (): Promise<void> => {
    if (!folder) return
    setBusy(true)
    try {
      setImported(await rpc.request<number>('import/vault', [folder, tag]))
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card" role="dialog" aria-label={t('vaultImport.title')}>
        <div className="dialog-title">{t('vaultImport.title')}</div>
        <p className="inspector-meta">{t('vaultImport.intro')}</p>

        <div className="export-field-row">
          <input className="dialog-input" readOnly value={folder} placeholder={t('vaultImport.noFolder')} />
          <button className="btn-secondary" onClick={() => void browse()}>
            <FolderOpen size={14} strokeWidth={2} /> {t('vaultImport.choose')}
          </button>
        </div>

        {/* What is there, before anything is brought in. Importing four hundred
            notes is not something to find out about afterwards. */}
        {scan && (
          <div className="cleanup-report">
            <p className="inspector-meta">{t('vaultImport.found', { count: scan.total })}</p>
            {scan.firstFew.length > 0 && (
              <ul className="cleanup-titles">
                {scan.firstFew.map((note) => (
                  <li key={note.relativePath}>{note.title}</li>
                ))}
              </ul>
            )}
            {scan.tags.length > 0 && (
              <p className="settings-hint">
                {t('vaultImport.tagsFound', { tags: scan.tags.slice(0, 12).join(', ') })}
              </p>
            )}
          </div>
        )}

        {scan && scan.total > 0 && (
          <label className="export-field">
            <span className="export-field-label">{t('vaultImport.extraTag')}</span>
            <input
              className="inspector-input"
              placeholder={t('vaultImport.extraTagPlaceholder')}
              value={tag}
              onChange={(e) => setTag(e.target.value)}
            />
          </label>
        )}

        {error && <p className="inspector-meta">{error}</p>}
        {imported !== null && (
          <p className="inspector-meta">{t('vaultImport.done', { count: imported })}</p>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('dialog.close')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy || !scan || scan.total === 0}
            onClick={() => void run()}
          >
            {t('vaultImport.run')}
          </button>
        </div>
      </div>
    </div>
  )
}
