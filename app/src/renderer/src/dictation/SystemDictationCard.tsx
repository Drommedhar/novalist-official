import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { showDictation, useDictation } from './dictationStore'

interface Status {
  engine: string; available: boolean; online: boolean; usesSystemPanel: boolean
  languages: { language: string; supported: boolean; installed: boolean }[]
}

export function SystemDictationCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [status, setStatus] = useState<Status | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(false)
  const [language, setLanguage] = useState('en')
  const pending = useRef<string | null>(null)
  const mounted = useRef(false)
  useEffect(() => {
    mounted.current = true
    void rpc.request<Status>('dictation/systemStatus').then((value) => {
      if (mounted.current) setStatus(value)
    }).catch(() => { if (mounted.current) setError(true) })
    return () => {
      mounted.current = false
      if (pending.current) void rpc.request('dictation/cancel', { requestId: pending.current }).catch(() => {})
    }
  }, [])
  const selected = status?.languages.find((entry) => entry.language === language)
  async function prepare(): Promise<void> {
    const requestId = crypto.randomUUID()
    pending.current = requestId
    setBusy(true); setError(false)
    try {
      await rpc.request('dictation/prepareSystem', { requestId, language })
      const refreshed = await rpc.request<Status>('dictation/systemStatus')
      if (mounted.current) setStatus(refreshed)
      if (useDictation.getState().open) await showDictation()
    } catch { if (mounted.current && pending.current) setError(true) }
    finally { pending.current = null; if (mounted.current) setBusy(false) }
  }
  return <div id="set-system-dictation" className="settings-subgroup">
    <label className="inspector-label">{t('dictation.systemName')}</label>
    <p className="settings-hint">{t(status?.usesSystemPanel ? 'dictation.windowsHelp' : 'dictation.appleHelp')}</p>
    {status && !status.available && <p className="settings-hint">{t('dictation.systemUnavailable')}</p>}
    {status?.available && !status.usesSystemPanel && <>
      <label className="inspector-label" htmlFor="set-system-dictation-language">{t('dictation.language')}</label>
      <select id="set-system-dictation-language" className="dialog-input" value={language} disabled={busy}
        onChange={(event) => setLanguage(event.target.value)}>
        <option value="en">English</option><option value="de">Deutsch</option>
      </select>
      <p className="settings-hint">{t(!selected?.supported ? 'dictation.languageUnsupported'
        : selected.installed ? 'dictation.languageReady' : 'dictation.languageDownload')}</p>
      {!busy && <button disabled={!selected?.supported || selected.installed} onClick={() => void prepare()}>{t('dictation.prepareSystem')}</button>}
      {busy && <>
        <span role="status">{t('dictation.preparingSystem')}</span>
        <button onClick={() => {
          const requestId = pending.current
          pending.current = null
          if (requestId) void rpc.request('dictation/cancel', { requestId }).catch(() => {})
        }}>{t('dialog.cancel')}</button>
      </>}
    </>}
    <p className="settings-hint">{t('dictation.systemFormatting')}</p>
    {error && <p role="alert">{t('dictation.systemFailed')}</p>}
  </div>
}
