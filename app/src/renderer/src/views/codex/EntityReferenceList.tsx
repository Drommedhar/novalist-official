import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import './entity-reference-list.css'

function selectedIds(value: string): string[] | null {
  try {
    const parsed: unknown = JSON.parse(value || '[]')
    return Array.isArray(parsed) && parsed.every((id): id is string => typeof id === 'string')
      ? [...new Set(parsed)] : null
  } catch {
    return null
  }
}

export function EntityReferenceList({ targetType, label, value, onChange, editable = true }: {
  targetType: string
  label: string
  value: string
  onChange: (value: string) => void | Promise<void>
  editable?: boolean
}): React.JSX.Element {
  const { t } = useTranslation()
  const [entries, setEntries] = useState<{ id: string; name: string }[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [pendingValue, setPendingValue] = useState<string | null>(null)
  const ids = selectedIds(pendingValue ?? value)
  useEffect(() => {
    let active = true
    setEntries([])
    setError('')
    void rpc.request<{ id: string; name: string }[]>('entities/list', [targetType])
      .then((items) => { if (active) setEntries(items) })
      .catch((cause: unknown) => { if (active) setError(String(cause)) })
    return () => { active = false }
  }, [targetType])

  const change = async (next: string[]): Promise<void> => {
    if (!editable) return
    const serialized = JSON.stringify(next)
    setPendingValue(serialized)
    setError('')
    try {
      const pending = onChange(serialized)
      if (pending) {
        setBusy(true)
        await pending
      }
    } catch (cause) {
      setError(String(cause))
    } finally {
      setPendingValue(null)
      setBusy(false)
    }
  }
  const choices = [...entries, ...(ids ?? [])
    .filter(id => !entries.some(entry => entry.id === id))
    .map(id => ({ id, name: t('entityTypeManager.unavailableReference') }))]
  return <div className="entity-reference-list" role="group" aria-label={label}>
    {error && <p role="alert">{error}</p>}
    {ids === null ? <>
      <p role="alert">{t('entityTypeManager.invalidReferenceList')}</p>
      <button className="dialog-button" onClick={() => void change([])} disabled={busy} aria-disabled={!editable}>
        {t('entityTypeManager.clearReferences')}
      </button>
    </> : choices.length === 0 ? <span>{t('codexHub.emptyHint')}</span> : choices.map(entry =>
      <label key={entry.id}>
        <input type="checkbox" checked={ids.includes(entry.id)} disabled={busy} aria-disabled={!editable}
          onChange={event => void change(event.target.checked ? [...ids, entry.id] : ids.filter(id => id !== entry.id))} />
        {entry.name}
      </label>
    )}
  </div>
}
