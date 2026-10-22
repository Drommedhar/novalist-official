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
  const [copy, setCopy] = useState('')
  const [name, setName] = useState('')
  const [error, setError] = useState('')
  const validName = name.trim().length > 0 && !/[\\/:*?"<>|]/.test(name) && !['.', '..'].includes(name.trim())
  useEffect(() => {
    onChange({ existing, path: existing ? copy : parent.replace(/[\\/]$/, '') + '/' + name.trim(),
      valid: existing ? Boolean(copy) : Boolean(parent) && validName })
  }, [existing, copy, parent, name, validName, onChange])
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
  return <>
    <label className="match-row" htmlFor={id + '-existing'}>
      <input id={id + '-existing'} type="checkbox" checked={existing} disabled={busy}
        onChange={event => setExisting(event.target.checked)} />
      {t('collaboration.useExisting')}
    </label>
    {!existing && <>
      <label className="inspector-label" htmlFor={id + '-name'}>{t('collaboration.localFolderName')}</label>
      <input id={id + '-name'} className="inspector-input" value={name} disabled={busy}
        onChange={event => setName(event.target.value)} />
    </>}
    <button className="btn-secondary" disabled={busy} onClick={() => void choose()}>
      {t(existing ? 'collaboration.existingFolder' : 'collaboration.parentFolder')}
    </button>
    {(existing ? copy : parent) && <p className="settings-hint">{existing ? copy : parent}</p>}
    {existing && <p className="settings-hint">{t('collaboration.reuseHint')}</p>}
    {error && <p role="alert">{error}</p>}
  </>
}
