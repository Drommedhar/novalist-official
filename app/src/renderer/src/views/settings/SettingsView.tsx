import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { availableLanguages } from '../../i18n'
import { useSettingsStore } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'

const THEMES = ['Default', 'Discord', 'Catppuccin Mocha']
const QUOTE_LANGUAGES = ['en', 'de-low', 'de-guillemet', 'fr', 'es', 'it', 'pt', 'ru', 'pl', 'cs', 'sk']

type Scope = 'global' | 'project'

export function SettingsView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const view = useSettingsStore((s) => s.view)
  const load = useSettingsStore((s) => s.load)
  const update = useSettingsStore((s) => s.update)
  const clearSection = useSettingsStore((s) => s.clearSection)
  const [appearanceScope, setAppearanceScope] = useState<Scope>('global')
  const [editorScope, setEditorScope] = useState<Scope>('global')
  const [writingScope, setWritingScope] = useState<Scope>('global')

  useEffect(() => {
    if (mainView !== 'settings') return
    void load()
  }, [mainView, load])

  if (!view) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const eff = view.effective

  const scopeToggle = (
    scope: Scope,
    setScope: (s: Scope) => void,
    section: 'appearance' | 'editor' | 'writing'
  ): React.JSX.Element | null =>
    view.hasProject ? (
      <label className="relationships-toggle settings-scope">
        <input
          type="checkbox"
          checked={scope === 'project'}
          onChange={(e) => {
            const next = e.target.checked ? 'project' : 'global'
            setScope(next)
            if (next === 'global') void clearSection(section)
          }}
        />
        {t('settings.scopeProjectOverride')}
      </label>
    ) : null

  const scopeFor = (scope: Scope): Scope => (view.hasProject ? scope : 'global')

  return (
    <div className="dashboard settings-view">
      <h1 className="dashboard-title">{t('settings.title')}</h1>

      <div className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('settings.appearance')}</div>
        {scopeToggle(appearanceScope, setAppearanceScope, 'appearance')}
        <label className="inspector-label" htmlFor="set-language">
          {t('settings.uiLanguage')}
        </label>
        <select
          id="set-language"
          className="dialog-input"
          value={eff.language}
          onChange={(e) =>
            void update(scopeFor(appearanceScope), { language: e.target.value })
          }
        >
          {availableLanguages().map((lang) => (
            <option key={lang.code} value={lang.code}>
              {lang.name}
            </option>
          ))}
        </select>
        <label className="inspector-label" htmlFor="set-theme">
          {t('settings.theme')}
        </label>
        <select
          id="set-theme"
          className="dialog-input"
          value={eff.theme === 'system' ? 'Default' : eff.theme}
          onChange={(e) => void update(scopeFor(appearanceScope), { theme: e.target.value })}
        >
          {THEMES.map((theme) => (
            <option key={theme} value={theme}>
              {theme}
            </option>
          ))}
        </select>
        <label className="inspector-label" htmlFor="set-accent">
          {t('settings.accentColor')}
        </label>
        <input
          id="set-accent"
          className="dialog-input settings-color"
          type="color"
          value={eff.accentColor ?? '#0e8bdf'}
          onChange={(e) => void update(scopeFor(appearanceScope), { accentColor: e.target.value })}
        />
      </div>

      <div className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('settings.editor')}</div>
        {scopeToggle(editorScope, setEditorScope, 'editor')}
        <label className="inspector-label" htmlFor="set-font">
          {t('settings.fontFamily')}
        </label>
        <input
          id="set-font"
          className="dialog-input"
          defaultValue={eff.editorFontFamily}
          onBlur={(e) =>
            void update(scopeFor(editorScope), { editorFontFamily: e.target.value })
          }
        />
        <label className="inspector-label" htmlFor="set-fontsize">
          {t('settings.fontSize')}
        </label>
        <input
          id="set-fontsize"
          className="dialog-input"
          type="number"
          min={8}
          max={36}
          value={eff.editorFontSize}
          onChange={(e) =>
            void update(scopeFor(editorScope), {
              editorFontSize: Math.min(36, Math.max(8, Number(e.target.value)))
            })
          }
        />
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={eff.typewriterScrollEnabled}
            onChange={(e) =>
              void update(scopeFor(editorScope), { typewriterScrollEnabled: e.target.checked })
            }
          />
          {t('settings.typewriterScroll')}
        </label>
        {eff.typewriterScrollEnabled && (
          <div className="findreplace-options">
            {['top', 'middle', 'bottom'].map((anchor) => (
              <label key={anchor} className="relationships-toggle">
                <input
                  type="radio"
                  name="typewriter-anchor"
                  checked={eff.typewriterScrollAnchor === anchor}
                  onChange={() =>
                    void update(scopeFor(editorScope), { typewriterScrollAnchor: anchor })
                  }
                />
                {t(`settings.typewriterAnchor${anchor.charAt(0).toUpperCase()}${anchor.slice(1)}`)}
              </label>
            ))}
          </div>
        )}
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={eff.pageViewEnabled}
            onChange={(e) =>
              void update(scopeFor(editorScope), { pageViewEnabled: e.target.checked })
            }
          />
          {t('settings.pageView')}
        </label>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={eff.enableBookParagraphSpacing}
            onChange={(e) =>
              void update(scopeFor(editorScope), { enableBookParagraphSpacing: e.target.checked })
            }
          />
          {t('settings.bookSpacing')}
        </label>
      </div>

      <div className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('settings.writingAssistance')}</div>
        {scopeToggle(writingScope, setWritingScope, 'writing')}
        <label className="inspector-label" htmlFor="set-quotes">
          {t('settings.quoteStyle')}
        </label>
        <select
          id="set-quotes"
          className="dialog-input"
          value={eff.autoReplacementLanguage}
          onChange={(e) =>
            void update(scopeFor(writingScope), { autoReplacementLanguage: e.target.value })
          }
        >
          {QUOTE_LANGUAGES.map((lang) => (
            <option key={lang} value={lang}>
              {lang}
            </option>
          ))}
        </select>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={eff.dialogueCorrectionEnabled}
            onChange={(e) =>
              void update(scopeFor(writingScope), { dialogueCorrectionEnabled: e.target.checked })
            }
          />
          {t('settings.dialogueCorrection')}
        </label>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={eff.grammarCheckEnabled}
            onChange={(e) =>
              void update(scopeFor(writingScope), { grammarCheckEnabled: e.target.checked })
            }
          />
          {t('settings.grammarCheck')}
        </label>
      </div>

      <div className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('settings.diagnostics')}</div>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={Boolean(view.global.diagnosticLoggingEnabled)}
            onChange={(e) => void update('global', { diagnosticLoggingEnabled: e.target.checked })}
          />
          {t('settings.diagnosticLogging')}
        </label>
      </div>
    </div>
  )
}
