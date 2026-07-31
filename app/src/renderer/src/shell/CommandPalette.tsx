import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { HotkeyAction } from './hotkeys'
import { onPluginContributionsChanged, pluginCommands } from './pluginHost'

interface CommandPaletteProps {
  actions: HotkeyAction[]
  onClose(): void
}

export function CommandPalette({ actions, onClose }: CommandPaletteProps): React.JSX.Element {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [index, setIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => inputRef.current?.focus(), [])

  // Commands extensions added, alongside Novalist's own. A plugin command that
  // could not be reached from here would be a command nobody could run.
  const [plugins, setPlugins] = useState([...pluginCommands()])
  useEffect(() => onPluginContributionsChanged(() => setPlugins([...pluginCommands()])), [])

  const all = useMemo<HotkeyAction[]>(
    () => [
      ...actions,
      ...plugins.map((command) => ({
        actionId: `${command.extensionId}:${command.id}`,
        // Already the extension's own words, so it is passed through rather
        // than looked up - a locale key would never resolve.
        labelKey: command.title,
        run: command.run
      })) as HotkeyAction[]
    ],
    [actions, plugins]
  )

  const filtered = useMemo(() => {
    const lower = query.toLowerCase()
    return all.filter(
      (action) =>
        lower.length === 0 ||
        t(action.labelKey).toLowerCase().includes(lower) ||
        action.actionId.toLowerCase().includes(lower)
    )
  }, [all, query, t])

  const run = (action: HotkeyAction): void => {
    onClose()
    action.run()
  }

  return (
    <div className="dialog-overlay palette-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card palette-card" role="dialog" aria-label={t('commandPalette.placeholder')}>
        <input
          ref={inputRef}
          className="dialog-input"
          placeholder={t('commandPalette.placeholder')}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setIndex(0)
          }}
          onKeyDown={(e) => {
            if (e.key === 'Escape') onClose()
            if (e.key === 'ArrowDown') setIndex((i) => Math.min(i + 1, filtered.length - 1))
            if (e.key === 'ArrowUp') setIndex((i) => Math.max(i - 1, 0))
            if (e.key === 'Enter' && filtered[index]) run(filtered[index])
          }}
        />
        <div className="palette-results">
          {filtered.map((action, i) => (
            <button
              key={action.actionId}
              className={`palette-item${i === index ? ' active' : ''}`}
              onClick={() => run(action)}
              onPointerEnter={() => setIndex(i)}
            >
              <span>{t(action.labelKey)}</span>
              <kbd>{action.gesture.replace('D', '')}</kbd>
            </button>
          ))}
          {filtered.length === 0 && <p className="codex-empty">{t('commandPalette.noResults')}</p>}
        </div>
      </div>
    </div>
  )
}
