import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RotateCcw } from 'lucide-react'
import { rpc } from '../../rpc/client'

/**
 * A token the editor offers, and how it is edited.
 *
 * The list is deliberate rather than scraped off the stylesheet: `tokens.css`
 * declares over a hundred, most of which are derived or internal, and a wall of
 * a hundred pickers is not an editor — it is a way of breaking the interface by
 * accident. These are the ones a writer would actually want to change.
 */
interface TokenSpec {
  name: string
  kind: 'colour' | 'size'
  group: 'surface' | 'text' | 'accent' | 'type' | 'shape'
}

// Every name here must exist in tokens.css. token-doctor only reads CSS, so a
// name invented in this list is not caught by it: the editor would show an
// empty field and setting it would paint a property nothing reads.
const TOKENS: TokenSpec[] = [
  { name: 'nl-surface-window', kind: 'colour', group: 'surface' },
  { name: 'nl-surface-card', kind: 'colour', group: 'surface' },
  { name: 'nl-surface-editor', kind: 'colour', group: 'surface' },
  { name: 'nl-surface-hover', kind: 'colour', group: 'surface' },
  { name: 'nl-border', kind: 'colour', group: 'surface' },
  { name: 'nl-text', kind: 'colour', group: 'text' },
  { name: 'nl-text-dim', kind: 'colour', group: 'text' },
  { name: 'nl-text-subtle', kind: 'colour', group: 'text' },
  { name: 'nl-accent', kind: 'colour', group: 'accent' },
  { name: 'nl-accent-hover', kind: 'colour', group: 'accent' },
  { name: 'nl-font-caption', kind: 'size', group: 'type' },
  { name: 'nl-font-body', kind: 'size', group: 'type' },
  { name: 'nl-font-title', kind: 'size', group: 'type' },
  { name: 'nl-radius-sm', kind: 'size', group: 'shape' },
  { name: 'nl-radius-md', kind: 'size', group: 'shape' },
  { name: 'nl-radius-lg', kind: 'size', group: 'shape' },
  { name: 'nl-space-sm', kind: 'size', group: 'shape' },
  { name: 'nl-space-md', kind: 'size', group: 'shape' },
  { name: 'nl-space-lg', kind: 'size', group: 'shape' }
]

const GROUPS: TokenSpec['group'][] = ['surface', 'text', 'accent', 'type', 'shape']

/** Sets or clears one token on the document, so a change is visible as it is made. */
function paint(name: string, value: string | null): void {
  const root = document.documentElement
  if (value) root.style.setProperty(`--${name}`, value)
  else root.style.removeProperty(`--${name}`)
}

/** Applies a whole override set. Called on load and after an import. */
export function applyThemeTokens(tokens: Record<string, string>): void {
  for (const spec of TOKENS) paint(spec.name, tokens[spec.name] ?? null)
}

/**
 * The value a token currently resolves to, with the overrides removed first.
 *
 * Read off the document rather than from a table: the theme decides it, and
 * duplicating every theme's palette here would be a second source of truth that
 * goes stale the moment somebody drops a theme file in their folder.
 */
function themeValue(name: string, overrides: Record<string, string>): string {
  const root = document.documentElement
  const had = overrides[name]
  if (had) root.style.removeProperty(`--${name}`)
  const resolved = getComputedStyle(root).getPropertyValue(`--${name}`).trim()
  if (had) root.style.setProperty(`--${name}`, had)
  return resolved
}

/**
 * Editing the design tokens without a text editor and a restart.
 *
 * Appearance offered Interface Language, Theme, Accent Color and two folder
 * buttons. Everything else — every surface, size and radius — meant hand-writing
 * a JSON token map or a `.css` file and restarting to see it.
 */
export function ThemeTokensCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [tokens, setTokens] = useState<Record<string, string>>({})
  const [loaded, setLoaded] = useState(false)
  const [importText, setImportText] = useState('')

  useEffect(() => {
    void rpc
      .request<Record<string, string>>('appearance/tokens')
      .then((stored) => {
        setTokens(stored ?? {})
        setLoaded(true)
      })
      .catch(() => setLoaded(true))
  }, [])

  const save = (next: Record<string, string>): void => {
    setTokens(next)
    applyThemeTokens(next)
    void rpc.request('appearance/setTokens', [next])
  }

  const set = (name: string, value: string): void => save({ ...tokens, [name]: value })

  const clear = (name: string): void => {
    const next = { ...tokens }
    delete next[name]
    // Painted before the save so the theme's own value is back immediately,
    // rather than after a round trip.
    paint(name, null)
    save(next)
  }

  const profile = useMemo(() => JSON.stringify(tokens, null, 2), [tokens])

  if (!loaded) return <div className="settings-hint">{t('themeTokens.loading')}</div>

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('themeTokens.intro')}</div>

      {GROUPS.map((group) => (
        <div key={group} className="settings-subgroup">
          <div className="inspector-label">{t(`themeTokens.group_${group}`)}</div>
          {TOKENS.filter((spec) => spec.group === group).map((spec) => {
            const current = tokens[spec.name] ?? themeValue(spec.name, tokens)
            return (
              <div key={spec.name} className="match-row">
                <span className="settings-hint token-name">{spec.name}</span>
                {spec.kind === 'colour' ? (
                  <input
                    className="dialog-input settings-color"
                    type="color"
                    aria-label={spec.name}
                    // A colour picker only understands hex. A theme that
                    // expresses a surface as a gradient or a named colour is
                    // shown as the nearest thing the control can hold, and
                    // editing it writes a hex - which is what the writer meant.
                    value={/^#[0-9a-f]{6}$/i.test(current) ? current : '#000000'}
                    onChange={(e) => set(spec.name, e.target.value)}
                  />
                ) : (
                  <input
                    className="inspector-input"
                    aria-label={spec.name}
                    value={current}
                    placeholder={themeValue(spec.name, tokens)}
                    onChange={(e) => set(spec.name, e.target.value)}
                  />
                )}
                {tokens[spec.name] && (
                  <button
                    className="binder-row-action"
                    aria-label={t('themeTokens.reset')}
                    title={t('themeTokens.reset')}
                    onClick={() => clear(spec.name)}
                  >
                    <RotateCcw size={15} strokeWidth={2} />
                  </button>
                )}
              </div>
            )
          })}
        </div>
      ))}

      {/* A profile is text, so it can be pasted into a message to somebody
          else. An import/export that needed a file dialog would not be. */}
      <div className="inspector-label">{t('themeTokens.profile')}</div>
      <textarea
        className="dialog-input token-profile"
        aria-label={t('themeTokens.profile')}
        value={importText || profile}
        onChange={(e) => setImportText(e.target.value)}
      />
      <div className="match-row">
        <button
          className="btn-secondary"
          disabled={importText.trim().length === 0}
          onClick={() => {
            try {
              const parsed = JSON.parse(importText) as Record<string, string>
              // Only known tokens: a profile from a newer version must not be
              // able to set arbitrary properties on the document.
              const next: Record<string, string> = {}
              for (const spec of TOKENS)
                if (typeof parsed[spec.name] === 'string') next[spec.name] = parsed[spec.name]
              save(next)
              setImportText('')
            } catch {
              // Invalid JSON leaves the box alone so the writer can fix it.
            }
          }}
        >
          {t('themeTokens.import')}
        </button>
        <button
          className="btn-secondary"
          disabled={Object.keys(tokens).length === 0}
          onClick={() => {
            for (const spec of TOKENS) paint(spec.name, null)
            save({})
            setImportText('')
          }}
        >
          {t('themeTokens.resetAll')}
        </button>
      </div>
    </div>
  )
}
