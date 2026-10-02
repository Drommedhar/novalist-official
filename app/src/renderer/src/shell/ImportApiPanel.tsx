import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy } from 'lucide-react'
import { rpc } from '../rpc/client'
import { flushPendingWrites } from '../stores/pendingWrites'
import { useProjectStore } from '../stores/projectStore'

interface ApiStatus {
  running: boolean
  url: string | null
  token: string | null
  book: string | null
  draft: string | null
  imported: number
  skipped: number
  failed: number
}

export function ImportApiPanel({ folder, disabled }: { folder: string; disabled: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const titleId = useId()
  const hintId = useId()
  const urlId = useId()
  const [status, setStatus] = useState<ApiStatus | null>(null)
  const [working, setWorking] = useState(false)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const updating = useRef(false)
  const panel = useRef<HTMLElement>(null)
  const showConnection = useRef(false)

  useEffect(() => {
    if (status?.running && showConnection.current) {
      showConnection.current = false
      panel.current?.scrollIntoView({ block: 'nearest' })
    }
  }, [status?.running])

  useEffect(() => {
    let active = true
    const refresh = (): void => {
      if (updating.current) return
      void rpc.request<ApiStatus>('importApi/status').then(result => {
        if (active && !updating.current) setStatus(result)
      }).catch(e => { if (active) setError(String(e)) })
    }
    refresh()
    const timer = setInterval(refresh, 2000)
    return () => { active = false; clearInterval(timer) }
  }, [])

  const toggle = async (): Promise<void> => {
    updating.current = true
    setWorking(true)
    setError(null)
    setCopied(false)
    try {
      if (status?.running) {
        await rpc.request('importApi/stop')
        setStatus(await rpc.request<ApiStatus>('importApi/status'))
      } else {
        showConnection.current = true
        await flushPendingWrites()
        await useProjectStore.getState().flushPendingSave()
        setStatus(await rpc.request<ApiStatus>('importApi/start', [t('folderImport.sectionTitle')]))
      }
    } catch (e) {
      showConnection.current = false
      setError(String(e))
    } finally {
      updating.current = false
      setWorking(false)
    }
  }

  const copy = (): void => {
    if (!status?.url || !status.token) return
    window.novalist.copyText(t('importApi.agentPrompt', {
      url: status.url, token: status.token, folder: folder || t('importApi.sourcePlaceholder'),
      interpolation: { escapeValue: false }
    }))
    setCopied(true)
  }

  return (
    <section ref={panel} className="folder-import-section folder-import-api" aria-labelledby={titleId}>
      <div className="folder-import-section-header">
        <h3 id={titleId}>{t('importApi.title')}</h3>
        <p className="folder-import-help">{t('importApi.hint')}</p>
      </div>
      <label className="folder-import-api-toggle">
        <span className="folder-import-api-toggle-label">
          <span>{t('importApi.enabled')}</span>
          <span className="folder-import-help" id={hintId}>{t(status?.running ? 'importApi.readyHint' : 'importApi.toggleHint')}</span>
        </span>
        <span className="folder-import-api-toggle-control">
          <span className="folder-import-api-state" aria-hidden="true">{t(working ? 'importApi.updating' : status?.running ? 'importApi.on' : 'importApi.off')}</span>
          <input type="checkbox" role="switch" className="folder-import-switch" aria-label={t('importApi.enabled')}
            aria-describedby={hintId} aria-busy={working} title={t(status?.running ? 'importApi.stop' : 'importApi.start')}
            checked={status?.running ?? false} disabled={disabled || working || !status} onChange={() => void toggle()} />
        </span>
      </label>
      {status?.running && <>
        <p className="folder-import-help">{t('importApi.destination', { book: status.book, draft: status.draft })}</p>
        <div className="export-field">
          <label className="export-field-label" htmlFor={urlId}>{t('importApi.url')}</label>
          <input id={urlId} className="dialog-input" readOnly value={status.url ?? ''} />
        </div>
        <div className="folder-import-section-actions">
          <button type="button" className="dialog-button" disabled={working} onClick={copy}>
            <Copy size="1em" aria-hidden="true" /> {t('importApi.copy')}
          </button>
        </div>
        {copied && <p className="folder-import-help" role="status">{t('importApi.copied')}</p>}
        <p className="folder-import-help">{t('importApi.lifetime')}</p>
        <p className="folder-import-help" role="status" aria-live="polite">{t('folderImport.result', {
          imported: status.imported, skipped: status.skipped, failed: status.failed
        })}</p>
      </>}
      {error && <p className="folder-import-help" role="alert">{error}</p>}
    </section>
  )
}
