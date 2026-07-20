import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RotateCcw } from 'lucide-react'
import {
  buildDefaultHotkeys,
  canonicalGesture,
  eventToGesture,
  type HotkeyAction
} from '../../shell/hotkeys'
import { useSettingsStore } from '../../stores/settingsStore'

/** Human-friendly gesture: "Ctrl+D1" reads as "Ctrl+1". */
function formatGesture(gesture: string): string {
  return gesture
    .split('+')
    .map((part) => (/^D[0-9]$/.test(part) ? part.slice(1) : part))
    .join('+')
}

export function HotkeysCard(): React.JSX.Element {
  const { t } = useTranslation()
  // Descriptors (id, default gesture, category, label) are stable; the live
  // gesture comes from the persisted overrides, read reactively below.
  const descriptors = useMemo<HotkeyAction[]>(() => buildDefaultHotkeys(), [])
  const bindings = useSettingsStore((s) => s.view?.global.hotkeyBindings) as
    | Record<string, string>
    | undefined
  const setHotkeyBinding = useSettingsStore((s) => s.setHotkeyBinding)
  const resetHotkeyBinding = useSettingsStore((s) => s.resetHotkeyBinding)
  const resetAllHotkeys = useSettingsStore((s) => s.resetAllHotkeys)

  const [filter, setFilter] = useState('')
  const [recordingId, setRecordingId] = useState<string | null>(null)
  const [conflict, setConflict] = useState<{ actionId: string; label: string } | null>(null)
  const recordRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (recordingId) recordRef.current?.focus()
  }, [recordingId])

  const gestureOf = (actionId: string, fallback: string): string =>
    bindings?.[actionId] ?? fallback

  const isModified = (d: HotkeyAction): boolean =>
    gestureOf(d.actionId, d.defaultGesture) !== d.defaultGesture

  const onRecordKeyDown = (event: React.KeyboardEvent, actionId: string): void => {
    event.preventDefault()
    event.stopPropagation()
    if (event.key === 'Escape') {
      setRecordingId(null)
      return
    }
    const gesture = eventToGesture(event.nativeEvent)
    if (!gesture) return // modifier-only press; keep listening
    const canon = canonicalGesture(gesture)
    const clash = descriptors.find(
      (d) => d.actionId !== actionId && canonicalGesture(gestureOf(d.actionId, d.defaultGesture)) === canon
    )
    setConflict(clash ? { actionId, label: t(clash.labelKey) } : null)
    setRecordingId(null)
    void setHotkeyBinding(actionId, gesture)
  }

  const query = filter.trim().toLowerCase()
  const groups = useMemo(() => {
    const map = new Map<string, HotkeyAction[]>()
    for (const d of descriptors) {
      const arr = map.get(d.categoryKey) ?? []
      arr.push(d)
      map.set(d.categoryKey, arr)
    }
    return [...map.entries()]
  }, [descriptors])

  const matches = (d: HotkeyAction): boolean =>
    query.length === 0 ||
    t(d.labelKey).toLowerCase().includes(query) ||
    t(d.categoryKey).toLowerCase().includes(query) ||
    gestureOf(d.actionId, d.defaultGesture).toLowerCase().includes(query)

  return (
    <section className="dashboard-card export-card settings-hotkeys">
      <div className="dashboard-card-title">{t('settings.hotkeys')}</div>
      <div className="settings-hotkeys-header">
        <input
          className="dialog-input settings-hotkeys-filter"
          placeholder={t('hotkeys.searchPlaceholder')}
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
        />
        <button className="dialog-button" onClick={() => void resetAllHotkeys()}>
          {t('hotkeys.resetAll')}
        </button>
      </div>

      {groups.map(([categoryKey, items]) => {
        const visible = items.filter(matches)
        if (visible.length === 0) return null
        return (
          <div key={categoryKey} className="settings-hotkeys-group">
            <div className="settings-hotkeys-category">{t(categoryKey)}</div>
            {visible.map((d) => {
              const current = gestureOf(d.actionId, d.defaultGesture)
              const recording = recordingId === d.actionId
              return (
                <div key={d.actionId} className="settings-hotkey-row">
                  <span className="settings-hotkey-name">{t(d.labelKey)}</span>
                  {conflict?.actionId === d.actionId && (
                    <span className="settings-hotkey-conflict">
                      {t('hotkeys.conflict').replace('{0}', conflict.label)}
                    </span>
                  )}
                  <button
                    ref={recording ? recordRef : undefined}
                    className={`settings-hotkey-gesture${recording ? ' recording' : ''}`}
                    onClick={() => {
                      setConflict(null)
                      setRecordingId(recording ? null : d.actionId)
                    }}
                    onKeyDown={recording ? (e) => onRecordKeyDown(e, d.actionId) : undefined}
                    onBlur={() => recording && setRecordingId(null)}
                  >
                    {recording ? t('hotkeys.pressKey') : <kbd>{formatGesture(current)}</kbd>}
                  </button>
                  <button
                    className="binder-expand"
                    aria-label={t('hotkeys.reset')}
                    disabled={!isModified(d)}
                    onClick={() => {
                      setConflict(null)
                      void resetHotkeyBinding(d.actionId)
                    }}
                  >
                    <RotateCcw size={13} strokeWidth={2} />
                  </button>
                </div>
              )
            })}
          </div>
        )
      })}
    </section>
  )
}
