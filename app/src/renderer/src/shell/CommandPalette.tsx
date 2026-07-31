import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { HotkeyAction } from './hotkeys'
import { onPluginContributionsChanged, pluginCommands } from './pluginHost'
import { rpc } from '../rpc/client'

interface CommandPaletteProps {
  actions: HotkeyAction[]
  onClose(): void
}

/**
 * A line in the palette. Novalist's own actions carry a gesture; commands that
 * come from an extension do not, and casting them to a hotkey to get them into
 * the list was how a missing gesture reached the renderer as undefined.
 */
interface PaletteEntry {
  actionId: string
  labelKey: string
  /** Already the extension's own words, so it is shown rather than looked up. */
  literal?: boolean
  gesture?: string
  run(): void
}

interface ExtensionCommand {
  id: string
  title: string
  description: string
  argumentsSchema: string
  mutates: boolean
}

/**
 * Whether a command cannot be run without being given something.
 *
 * The schema is documentation rather than a contract the host enforces, so an
 * unreadable one is not a reason to hide the command - only a `required` list
 * with names in it is.
 */
function requiresArguments(schema: string): boolean {
  if (schema.trim().length === 0) return false
  try {
    const required = (JSON.parse(schema) as { required?: unknown }).required
    return Array.isArray(required) && required.length > 0
  } catch {
    return false
  }
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

  // And the ones backend extensions registered. Fetched when the palette opens
  // rather than held in a store: an extension can register and unregister as it
  // loads, and a list read once at boot goes stale without ever looking wrong.
  const [extensionCommands, setExtensionCommands] = useState<ExtensionCommand[]>([])
  useEffect(() => {
    let cancelled = false
    void rpc
      .request<ExtensionCommand[]>('extensions/commands')
      .then((list) => {
        if (!cancelled) setExtensionCommands(list ?? [])
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  const all = useMemo<PaletteEntry[]>(
    () => [
      ...actions,
      ...plugins.map((command) => ({
        actionId: `${command.extensionId}:${command.id}`,
        labelKey: command.title,
        literal: true,
        run: command.run
      })),
      // A command that *needs* an argument has no way to ask for it here, so it
      // is left out rather than offered as a line that fails when clicked.
      // Having a schema is not the same as needing one: nearly every command
      // declares optional flags a script may pass, and skipping those hid whole
      // extensions from the palette.
      ...extensionCommands
        .filter((command) => !requiresArguments(command.argumentsSchema))
        .map((command) => ({
          actionId: command.id,
          labelKey: command.title,
          literal: true,
          run: () => {
            void rpc.request('extensions/command/execute', [command.id, null]).catch(() => {})
          }
        }))
    ],
    [actions, plugins, extensionCommands]
  )

  const label = (entry: PaletteEntry): string => (entry.literal ? entry.labelKey : t(entry.labelKey))

  const filtered = useMemo(() => {
    const lower = query.toLowerCase()
    return all.filter(
      (action) =>
        lower.length === 0 ||
        label(action).toLowerCase().includes(lower) ||
        action.actionId.toLowerCase().includes(lower)
    )
  }, [all, query, t])

  const run = (action: PaletteEntry): void => {
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
              <span>{label(action)}</span>
              {action.gesture && <kbd>{action.gesture.replace('D', '')}</kbd>}
            </button>
          ))}
          {filtered.length === 0 && <p className="codex-empty">{t('commandPalette.noResults')}</p>}
        </div>
      </div>
    </div>
  )
}
