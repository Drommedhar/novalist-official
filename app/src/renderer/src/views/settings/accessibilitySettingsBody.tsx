import { type SettingsBodyContext } from './settingsBodyContext'
import { SettingInput, SettingNumber } from './SettingsInputs'

export function accessibilitySettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { t, tipsEnabled, setTipsEnabled, eff, update, scopeFor } = context
  return (
    <>
      <div className="settings-desc">{t('settings.accessibilityDesc')}</div>

      <label className="relationships-toggle">
        <input
          id="set-contextual-tips"
          type="checkbox"
          checked={tipsEnabled}
          onChange={(event) => setTipsEnabled(event.target.checked)}
        />
        {t('settings.contextualTips')}
      </label>
      <div className="settings-hint">{t('settings.contextualTipsDesc')}</div>

      {/* The same three settings the Editor section has, gathered where
              somebody looking for them would look. A dyslexia-friendly face is
              not a typography preference to the person who needs one. */}
      <label className="inspector-label" htmlFor="set-a11y-font">
        {t('settings.accessibleFont')}
      </label>
      <SettingInput
        id="set-a11y-font"
        list="settings-fonts"
        value={eff.editorFontFamily}
        onCommit={(v) => void update(scopeFor('editor'), { editorFontFamily: v })}
      />
      <div className="settings-hint">{t('settings.accessibleFontHint')}</div>

      <label className="inspector-label" htmlFor="set-a11y-size">
        {t('settings.fontSize')}
      </label>
      <SettingNumber
        id="set-a11y-size"
        min={8}
        max={36}
        value={eff.editorFontSize}
        onCommit={(v) => void update(scopeFor('editor'), { editorFontSize: v })}
      />

      <label className="inspector-label" htmlFor="set-a11y-spacing">
        {t('settings.lineHeight')}
      </label>
      <SettingNumber
        id="set-a11y-spacing"
        min={1}
        max={2.5}
        step={0.1}
        value={eff.editorLineHeight}
        onCommit={(v) => void update(scopeFor('editor'), { editorLineHeight: v })}
      />
      <div className="settings-hint">{t('settings.lineHeightDesc')}</div>

      {/* One click rather than a paragraph telling somebody where the
              theme picker is.
              The theme belongs to Appearance, so it is written wherever
              Appearance is written. Hardcoding "global" here did nothing at all
              for a project that pins its own appearance: the write landed in
              the app-level settings and the project's override went on
              shadowing it, so the button reported nothing and changed
              nothing. */}
      <button
        id="set-high-contrast"
        className="dialog-button"
        onClick={() => void update(scopeFor('appearance'), { theme: 'High Contrast' })}
      >
        {t('settings.useHighContrast')}
      </button>
      <div className="settings-hint">{t('settings.highContrastHint')}</div>
    </>
  )
}
