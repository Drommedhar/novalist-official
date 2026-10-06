import { FolderOpen } from 'lucide-react'
import { availableLanguages } from '../../i18n'
import { DEFAULT_UI_SCALE, UI_SCALE_STEPS } from '../../stores/uiScaleStore'
import { type SettingsBodyContext } from './settingsBodyContext'

export function appearanceSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { scopeToggle, t, eff, update, scopeFor, themes } = context
  return (
    <>
      {scopeToggle('appearance')}
      <label className="inspector-label" htmlFor="set-language">
        {t('settings.uiLanguage')}
      </label>
      <select
        id="set-language"
        className="dialog-input"
        value={eff.language}
        onChange={(e) => void update(scopeFor('appearance'), { language: e.target.value })}
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
        onChange={(e) => void update(scopeFor('appearance'), { theme: e.target.value })}
      >
        {themes.map((theme) => (
          <option key={`${theme.origin}:${theme.slug}`} value={theme.name}>
            {theme.name}
          </option>
        ))}
      </select>
      {scaleAndAssetSettings(context)}
      <label className="inspector-label" htmlFor="set-accent">
        {t('settings.accentColor')}
      </label>
      <div className="settings-accent-row">
        <input
          id="set-accent"
          className="dialog-input settings-color"
          type="color"
          value={eff.accentColor ?? '#0e8bdf'}
          onChange={(e) => void update(scopeFor('appearance'), { accentColor: e.target.value })}
        />
        <button
          className="dialog-button"
          onClick={() => void update(scopeFor('appearance'), { accentColor: null })}
        >
          {t('settings.accentColorReset')}
        </button>
      </div>
    </>
  )
}

function scaleAndAssetSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, uiScale, setUiScale, resetUiScale, assetDirs } = context
  return <><label className="inspector-label" htmlFor="set-ui-scale">
    {t('settings.uiScale')}
  </label>
    <div className="settings-button-row">
      <select
        id="set-ui-scale"
        className="dialog-input"
        value={uiScale}
        onChange={(event) => setUiScale(Number(event.target.value))}
      >
        {UI_SCALE_STEPS.map((percent) => (
          <option key={percent} value={percent}>
            {percent}%
          </option>
        ))}
      </select>
      <button
        className="dialog-button"
        disabled={uiScale === DEFAULT_UI_SCALE}
        onClick={resetUiScale}
      >
        {t('settings.uiScaleReset')}
      </button>
    </div>
    <p className="settings-hint">{t('settings.uiScaleDesc')}</p>
    <p className="settings-hint">{t('settings.customAssetsHint')}</p>
    <div className="settings-accent-row">
      <button
        className="dialog-button"
        disabled={!assetDirs}
        onClick={() => void window.novalist.revealPath(assetDirs!.themes)}
      >
        <FolderOpen size={13} strokeWidth={2} /> {t('settings.openThemesFolder')}
      </button>
      <button
        className="dialog-button"
        disabled={!assetDirs}
        onClick={() => void window.novalist.revealPath(assetDirs!.locales)}
      >
        <FolderOpen size={13} strokeWidth={2} /> {t('settings.openLocalesFolder')}
      </button>
    </div></>
}
