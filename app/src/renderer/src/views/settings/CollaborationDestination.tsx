import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

export interface CollaborationDestinationValue { path: string; valid: boolean; existing: boolean }

export function CollaborationDestination({ id, busy, onChange }: {
  id: string
  busy: boolean
  onChange(value: CollaborationDestinationValue): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [existing, setExisting] = useState(false)
  const [parent, setParent] = useState('')
  const [defaultRoot, setDefaultRoot] = useState('')
  const [copy, setCopy] = useState('')
  const [name, setName] = useState(() => t('collaboration.defaultFolderName'))
  const [error, setError] = useState('')
  useEffect(() => {
    if (!window.novalist.isMobile || !window.novalist.defaultProjectRoot) return
    let current = true
    void window.novalist.defaultProjectRoot().then(folder => {
      if (current && folder) { setParent(folder); setDefaultRoot(folder) }
    }).catch(reason => { if (current) setError(String(reason)) })
    return () => { current = false }
  }, [])
  const validName = name.trim().length > 0 && !/[\\/:*?"<>|\x00-\x1f]/.test(name) && !/[. ]$/.test(name)
    && !/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name)
  const path = existing ? copy : parent.replace(/[\\/]$/, '') + '/' + name.trim()
  const displayPath = !existing && defaultRoot && parent === defaultRoot
    ? t('collaboration.mobileFolder', { name: name.trim() }) : path
  useEffect(() => {
    onChange({ existing, path,
      valid: existing ? Boolean(copy) : Boolean(parent) && validName })
  }, [existing, copy, parent, path, validName, onChange])
  const choose = async (): Promise<void> => {
    try {
      const folder = await window.novalist.pickFolder(t(existing ? 'collaboration.existingFolder' : 'collaboration.parentFolder'))
      if (folder) {
        if (existing) setCopy(folder)
        else setParent(folder)
        setError('')
      }
    } catch (reason) { setError(String(reason)) }
  }
  return <div className="collaboration-destination">
    <label className="collaboration-checkbox" htmlFor={id + '-existing'}>
      <input id={id + '-existing'} type="checkbox" checked={existing} disabled={busy}
        onChange={event => setExisting(event.target.checked)} />
      {t('collaboration.useExisting')}
    </label>
    {!existing && <>
      <label className="inspector-label" htmlFor={id + '-name'}>{t('collaboration.localFolderName')}</label>
      <input id={id + '-name'} className="dialog-input" value={name} disabled={busy}
        aria-invalid={!validName} aria-describedby={!validName ? id + '-invalid' : undefined}
        onChange={event => setName(event.target.value)} />
      {!validName && <p id={id + '-invalid'} className="collaboration-error" role="alert">{t('collaboration.invalidFolderName')}</p>}
    </>}
    <button className="dialog-button" disabled={busy} onClick={() => void choose()}>
      {t(existing ? 'collaboration.existingFolder' : 'collaboration.parentFolder')}
    </button>
    <p className="settings-hint collaboration-destination-path">{(existing ? copy : parent) ? displayPath : t('collaboration.chooseDestinationHint')}</p>
    {existing && <p className="settings-hint">{t('collaboration.reuseHint')}</p>}
    {error && <p role="alert">{error}</p>}
  </div>
}
