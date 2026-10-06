import ReactMarkdown from 'react-markdown'
import { type SettingsBodyContext } from './settingsBodyContext'
import { SettingInput, SettingNumber } from './SettingsInputs'
import { PAGE_FORMATS, estimateCharsPerLine } from './settingsTypography'

export function editorSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { scopeToggle, fontDatalist, t, eff, update, scopeFor } = context
  return (
    <>
      {scopeToggle('editor')}
      {fontDatalist}
      {editorFontSettings(context)}
      {editorSpacingSettings(context)}
      {readAloudSettings(context)}
      {editorFocusSettings(context)}
      <label className="relationships-toggle">
        <input
          id="set-page-view"
          type="checkbox"
          checked={eff.pageViewEnabled}
          onChange={(e) =>
            void update(scopeFor('editor'), { pageViewEnabled: e.target.checked })
          }
        />
        {t('settings.pageView')}
      </label>
      <label className="relationships-toggle">
        <input
          id="set-book-spacing"
          type="checkbox"
          checked={eff.enableBookParagraphSpacing}
          onChange={(e) =>
            void update(scopeFor('editor'), { enableBookParagraphSpacing: e.target.checked })
          }
        />
        {t('settings.bookSpacing')}
      </label>

      {bookWidthSettings(context)}
    </>
  )
}

function editorFontSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor } = context
  return <><label className="inspector-label" htmlFor="set-font">
    {t('settings.fontFamily')}
  </label>
    <SettingInput
      id="set-font"
      list="settings-fonts"
      value={eff.editorFontFamily}
      onCommit={(v) => void update(scopeFor('editor'), { editorFontFamily: v })}
    />
    <label className="inspector-label" htmlFor="set-fontsize">
      {t('settings.fontSize')}
    </label>
    <SettingNumber
      id="set-fontsize"
      min={8}
      max={36}
      value={eff.editorFontSize}
      onCommit={(v) => void update(scopeFor('editor'), { editorFontSize: v })}
    />
    <label className="inspector-label" htmlFor="set-lineheight">
      {t('settings.lineHeight')}
    </label>
    <SettingNumber
      id="set-lineheight"
      min={1}
      max={2.5}
      step={0.05}
      value={eff.editorLineHeight}
      onCommit={(v) => void update(scopeFor('editor'), { editorLineHeight: v })}
    />
    <div className="settings-hint">{t('settings.lineHeightDesc')}</div></>
}

function editorSpacingSettings(context: SettingsBodyContext): React.ReactNode {
  return <>
    {editorSpacingField(context, { key: 'editorLetterSpacing', id: 'set-letterspacing', label: 'letterSpacing', min: -1, max: 4, step: 0.1 })}
    {editorSpacingField(context, { key: 'editorParagraphSpacing', id: 'set-paraspacing', label: 'paragraphSpacing', min: 0, max: 3, step: 0.05 })}
    {editorSpacingField(context, { key: 'editorFirstLineIndent', id: 'set-first-line-indent', label: 'firstLineIndent', min: 0, max: 4, step: 0.1 })}
  </>
}
interface SpacingField {
  key: 'editorLetterSpacing' | 'editorParagraphSpacing' | 'editorFirstLineIndent'
  id: string; label: string; min: number; max: number; step: number
}
function editorSpacingField(context: SettingsBodyContext, field: SpacingField): React.ReactNode {
  const { t, eff, update, scopeFor } = context
  return <>
    <label className="inspector-label" htmlFor={field.id}>{t('settings.' + field.label)}</label>
    <SettingNumber id={field.id} min={field.min} max={field.max} step={field.step} value={eff[field.key]}
      onCommit={(value) => void update(scopeFor('editor'), { [field.key]: value })} />
    <div className="settings-hint">{t('settings.' + field.label + 'Desc')}</div>
  </>
}

function readAloudSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor, systemVoices, voices, writingLanguage } = context
  return <><label className="inspector-label" htmlFor="set-readaloud-rate">
    {t('settings.readAloudRate')}
  </label>
    <SettingNumber
      id="set-readaloud-rate"
      min={0.5}
      max={2}
      step={0.1}
      value={eff.readAloudRate}
      onCommit={(v) => void update(scopeFor('editor'), { readAloudRate: v })}
    />
    <label className="inspector-label" htmlFor="set-readaloud-voice">
      {t('settings.readAloudVoice')}
    </label>
    <select
      id="set-readaloud-voice"
      className="dialog-input"
      value={eff.readAloudVoiceUri ?? ''}
      onChange={(e) =>
        void update(scopeFor('editor'), { readAloudVoiceUri: e.target.value || null })
      }
    >
      <option value="">{t('settings.readAloudVoiceAuto')}</option>
      {/* The system engine's voices when it has any, the browser's
                otherwise. Not both: the same voice under two ids reads as two
                voices, and picking the wrong one plays nothing. */}
      {systemVoices.length > 0
        ? systemVoices.map((v) => (
          <option key={v.id} value={v.id}>
            {v.name}
          </option>
        ))
        : voices.map((v) => (
          <option key={v.voiceURI} value={v.voiceURI}>
            {v.name} ({v.lang})
          </option>
        ))}
    </select>
    <div className="settings-hint">{t('settings.readAloudDesc')}</div>
    {/* Windows has two kinds of voice and only one is reachable by an
              application. The ones added under Narrator's "natural voices" are
              Narrator's alone - not through SAPI, not through WinRT, not
              through Chromium - so a writer who installs one there waits
              forever for it to appear. Naming the trap is the only fix
              available to us, because there is no API that reaches them. */}
    {systemVoices.length === 0 && voices.length === 0 ? (
      <div className="settings-hint export-warning">{t('settings.readAloudNoVoices')}</div>
    ) : (
      !(systemVoices.length > 0 ? systemVoices : voices).some((v) =>
        ('language' in v ? v.language : v.lang)
          .toLowerCase()
          .startsWith(writingLanguage.slice(0, 2).toLowerCase())
      ) && (
        <div className="settings-hint export-warning">
          {t('settings.readAloudNoMatchingVoice', { language: writingLanguage })}
        </div>
      )
    )}
    {window.novalist.platform === 'win32' && (
      <details className="settings-help-disclosure">
        <summary>{t('settings.readAloudHelpSummary')}</summary>
        <div className="settings-help-copy">
          <ReactMarkdown>{t('settings.readAloudVoiceKinds')}</ReactMarkdown>
        </div>
      </details>
    )}</>
}

function editorFocusSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor, isMobile } = context
  return <>{/* Typewriter scroll makes no sense on a phone (and is force-disabled in
              the mobile editor), so hide it there. */}
    {!isMobile && (
      <>
        <label className="relationships-toggle">
          <input
            id="set-compose-dimming"
            type="checkbox"
            checked={eff.composeDimming}
            onChange={(e) =>
              void update(scopeFor('editor'), { composeDimming: e.target.checked })
            }
          />
          {t('settings.composeDimming')}
        </label>
        <div className="settings-hint">{t('settings.composeDimmingDesc')}</div>
        <label className="relationships-toggle">
          <input
            id="set-typewriter-scroll"
            type="checkbox"
            checked={eff.typewriterScrollEnabled}
            onChange={(e) =>
              void update(scopeFor('editor'), { typewriterScrollEnabled: e.target.checked })
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
                    void update(scopeFor('editor'), { typewriterScrollAnchor: anchor })
                  }
                />
                {t(
                  `settings.typewriterAnchor${anchor.charAt(0).toUpperCase()}${anchor.slice(1)}`
                )}
              </label>
            ))}
          </div>
        )}
      </>
    )}</>
}

function bookWidthSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor, isMobile } = context
  return <>{/* Book width targets a wide desktop editor; it has no effect on a
              phone's narrow column, so hide it on mobile. */}
    {!isMobile && (
      <>
        <label className="relationships-toggle">
          <input
            id="set-book-width"
            type="checkbox"
            checked={eff.enableBookWidth}
            onChange={(e) =>
              void update(scopeFor('editor'), { enableBookWidth: e.target.checked })
            }
          />
          {t('settings.bookWidth')}
        </label>
        {eff.enableBookWidth && (
          <div className="settings-subgroup">
            <label className="inspector-label" htmlFor="set-pageformat">
              {t('settings.bookWidthPageFormat')}
            </label>
            <select
              id="set-pageformat"
              className="dialog-input"
              value={eff.bookPageFormat}
              onChange={(e) =>
                void update(scopeFor('editor'), { bookPageFormat: e.target.value })
              }
            >
              {PAGE_FORMATS.map((f) => (
                <option key={f.code} value={f.code}>
                  {f.name}
                </option>
              ))}
            </select>
            {eff.bookPageFormat === 'Custom' && (
              <>
                <label className="inspector-label" htmlFor="set-customwidth">
                  {t('settings.bookWidthCustom')}
                </label>
                <SettingNumber
                  id="set-customwidth"
                  min={1}
                  max={12}
                  step={0.05}
                  value={eff.bookTextBlockWidth ?? 4.75}
                  onCommit={(v) => void update(scopeFor('editor'), { bookTextBlockWidth: v })}
                />
              </>
            )}
            <label className="inspector-label" htmlFor="set-bookfont">
              {t('settings.bookWidthFont')}
            </label>
            <SettingInput
              id="set-bookfont"
              list="settings-fonts"
              value={eff.bookFontFamily}
              onCommit={(v) => void update(scopeFor('editor'), { bookFontFamily: v })}
            />
            <label className="inspector-label" htmlFor="set-bookfontsize">
              {t('settings.bookWidthFontSize')}
            </label>
            <SettingNumber
              id="set-bookfontsize"
              min={6}
              max={24}
              value={eff.bookFontSize}
              onCommit={(v) => void update(scopeFor('editor'), { bookFontSize: v })}
            />
            <div className="settings-preview">
              {t('settings.bookWidthCharsPerLine', {
                count: estimateCharsPerLine(
                  eff.bookPageFormat,
                  eff.bookTextBlockWidth,
                  eff.bookFontFamily,
                  eff.bookFontSize
                )
              })}
            </div>
          </div>
        )}
      </>
    )}</>
}
