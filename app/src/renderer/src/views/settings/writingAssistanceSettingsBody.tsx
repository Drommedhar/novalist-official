import { ExternalLink } from 'lucide-react'
import { SystemDictationCard } from '../../dictation/SystemDictationCard'
import { AutoReplacementsCard } from './AutoReplacementsCard'
import { type SettingsBodyContext } from './settingsBodyContext'
import { SettingInput } from './SettingsInputs'
import { autoReplacementPreview } from './settingsQuoteStyles'
import { SpellCheckCard } from './SpellCheckCard'
import { WatchWordsCard } from './WatchWordsCard'

export function writingAssistanceSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { scopeToggle } = context
  return (
    <>
      <SystemDictationCard />
      {scopeToggle('writing')}
      {replacementSettings(context)}
      {reviewAndSpellingSettings(context)}
      {grammarSettings(context)}
    </>
  )
}

function replacementSettings(context: SettingsBodyContext): React.ReactNode {
  const { eff, update, scopeFor, t, writingLanguages } = context
  return <>{/* The master switch comes before the style it governs. Off, nothing
              is substituted as you type - the quote style below still names the
              language the book is written in, which export, grammar, spelling
              and the statistics all read. */}
    <label className="relationships-toggle">
      <input
        id="set-auto-replacement"
        type="checkbox"
        checked={eff.autoReplacementEnabled}
        onChange={(e) =>
          void update(scopeFor('writing'), { autoReplacementEnabled: e.target.checked })
        }
      />
      {t('settings.autoReplacement')}
    </label>
    <div className="settings-hint">{t('settings.autoReplacementDesc')}</div>
    <label className="inspector-label" htmlFor="set-quotes">
      {t('settings.quoteStyle')}
    </label>
    <select
      id="set-quotes"
      className="dialog-input"
      value={eff.autoReplacementLanguage}
      onChange={(e) =>
        void update(scopeFor('writing'), { autoReplacementLanguage: e.target.value })
      }
    >
      {writingLanguages.map((lang) => (
        <option key={lang} value={lang}>
          {lang}
        </option>
      ))}
    </select>
    <div className="settings-preview">
      {t('settings.preview')}:{' '}
      {eff.autoReplacementEnabled
        ? autoReplacementPreview(eff.autoReplacementLanguage)
        : t('settings.autoReplacementOffPreview')}
    </div>
    <div className="settings-hint">{t('settings.quoteStyleReseeds')}</div>
    <AutoReplacementsCard scope={scopeFor('writing')} /></>
}

function reviewAndSpellingSettings(context: SettingsBodyContext): React.ReactNode {
  const { eff, update, scopeFor, t, view } = context
  return <><label className="inspector-label" htmlFor="set-reviewer">
    {t('settings.reviewerName')}
  </label>
    <input
      id="set-reviewer"
      className="dialog-input"
      type="text"
      value={eff.reviewerName}
      placeholder={t('settings.reviewerNamePlaceholder')}
      onChange={(e) => void update(scopeFor('writing'), { reviewerName: e.target.value })}
    />
    <div className="settings-hint">{t('settings.reviewerNameHint')}</div>
    <label className="relationships-toggle">
      <input
        id="set-dialogue-correction"
        type="checkbox"
        checked={eff.dialogueCorrectionEnabled}
        onChange={(e) =>
          void update(scopeFor('writing'), { dialogueCorrectionEnabled: e.target.checked })
        }
      />
      {t('settings.dialogueCorrection')}
    </label>
    {/* Spelling first: it needs no server and works offline, so it is the
              one a writer should meet before the network-bound grammar check. */}
    <label className="relationships-toggle">
      <input
        id="set-spell-check"
        type="checkbox"
        checked={eff.spellCheckEnabled}
        onChange={(e) =>
          void update(scopeFor('writing'), { spellCheckEnabled: e.target.checked })
        }
      />
      {t('settings.spellCheck')}
    </label>
    <div className="settings-hint">{t('settings.spellCheckHint')}</div>
    <SpellCheckCard
      enabled={eff.spellCheckEnabled}
      languages={
        (view.overrides?.spellCheckLanguages ??
          view.global.spellCheckLanguages ??
          []) as string[]
      }
      onLanguagesChange={(languages) =>
        void update(scopeFor('writing'), { spellCheckLanguages: languages })
      }
    />
    {/* Words the writer wants counted, beside the checks Novalist
              brings. Their habits follow them, so this is a global list. */}
    <WatchWordsCard /></>
}

function grammarSettings(context: SettingsBodyContext): React.ReactNode {
  const { eff, update, scopeFor, t } = context
  return <><label className="relationships-toggle">
    <input
      id="set-grammar-check"
      type="checkbox"
      checked={eff.grammarCheckEnabled}
      onChange={(e) =>
        void update(scopeFor('writing'), { grammarCheckEnabled: e.target.checked })
      }
    />
    {t('settings.grammarCheck')}
  </label>
    {eff.grammarCheckEnabled && (
      <div className="settings-subgroup">
        <label className="inspector-label" htmlFor="set-gc-provider">
          {t('settings.grammarCheckProvider')}
        </label>
        <select
          id="set-gc-provider"
          className="dialog-input"
          value={eff.grammarCheckProvider}
          onChange={(e) =>
            void update(scopeFor('writing'), { grammarCheckProvider: e.target.value })
          }
        >
          <option value="languagetool">LanguageTool</option>
          <option value="harper">{t('settings.grammarCheckHarper')}</option>
        </select>
        <div className="settings-hint">
          {t(
            eff.grammarCheckProvider === 'harper'
              ? 'settings.grammarCheckHarperDesc'
              : 'settings.grammarCheckDesc'
          )}
        </div>
        {eff.grammarCheckProvider === 'harper' &&
          !/^en(?:-|$)/i.test(eff.grammarCheckLanguage) && (
            <div className="settings-hint">{t('settings.grammarCheckHarperEnglishOnly')}</div>
          )}
        {eff.grammarCheckProvider !== 'harper' && (
          <>
            {grammarConnectionSettings(context)}
            <button
              className="dialog-button settings-link"
              onClick={() =>
                void window.novalist.openExternal(
                  'https://languagetool.org/editor/settings/access-tokens'
                )
              }
            >
              <ExternalLink size={13} strokeWidth={2} /> {t('settings.grammarCheckGetApiKey')}
            </button>
            {grammarOptionsSettings(context)}
          </>
        )}
      </div>
    )}</>
}

function grammarConnectionSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor } = context
  return <><label className="inspector-label" htmlFor="set-gc-url">
    {t('settings.grammarCheckApiUrl')}
  </label>
    <SettingInput
      id="set-gc-url"
      value={eff.grammarCheckApiUrl ?? ''}
      placeholder={
        // aislop-ignore-next-line ai-slop/hardcoded-url -- Editable endpoint example; requests use the saved setting, not this placeholder.
        'https://api.languagetool.org/v2/check'
      }
      onCommit={(v) =>
        void update(scopeFor('writing'), { grammarCheckApiUrl: v.trim() || null })
      }
    />
    <label className="inspector-label" htmlFor="set-gc-user">
      {t('settings.grammarCheckUsername')}
    </label>
    <SettingInput
      id="set-gc-user"
      value={eff.grammarCheckUsername ?? ''}
      placeholder={t('settings.grammarCheckUsernamePlaceholder')}
      onCommit={(v) =>
        void update(scopeFor('writing'), { grammarCheckUsername: v.trim() || null })
      }
    />
    <label className="inspector-label" htmlFor="set-gc-key">
      {t('settings.grammarCheckApiKey')}
    </label>
    <SettingInput
      id="set-gc-key"
      type="password"
      value={eff.grammarCheckApiKey ?? ''}
      onCommit={(v) =>
        void update(scopeFor('writing'), { grammarCheckApiKey: v.trim() || null })
      }
    /></>
}

function grammarOptionsSettings(context: SettingsBodyContext): React.ReactNode {
  const { t, eff, update, scopeFor } = context
  return <><label className="relationships-toggle">
    <input
      id="set-gc-picky"
      type="checkbox"
      checked={eff.grammarCheckPickyMode}
      onChange={(e) =>
        void update(scopeFor('writing'), {
          grammarCheckPickyMode: e.target.checked
        })
      }
    />
    {t('settings.grammarCheckPickyMode')}
  </label>
    <label className="inspector-label" htmlFor="set-gc-mother">
      {t('settings.grammarCheckMotherTongue')}
    </label>
    <select
      id="set-gc-mother"
      className="dialog-input"
      value={eff.grammarCheckMotherTongue ?? ''}
      onChange={(e) =>
        void update(scopeFor('writing'), {
          grammarCheckMotherTongue: e.target.value || null
        })
      }
    >
      <option value="">{t('settings.grammarCheckMotherTongueNone')}</option>
      {['en', 'de', 'fr', 'es', 'it', 'pt', 'nl', 'pl', 'ru', 'zh', 'ja'].map(
        (code) => (
          <option key={code} value={code}>
            {code}
          </option>
        )
      )}
    </select></>
}
