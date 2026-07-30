import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ExternalLink, FolderOpen } from 'lucide-react'
import { availableLanguages } from '../../i18n'
import { rpc } from '../../rpc/client'
import { useSettingsStore, type SettingsSection } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'
import { TargetsPanel } from '../dashboard/TargetsCard'
import { useProjectStore } from '../../stores/projectStore'
import { useThemeCatalog } from '../../stores/themeCatalog'
import { assetDirectories } from '../../stores/userAssets'
import { TemplatesCard } from './TemplatesCard'
import { HotkeysCard } from './HotkeysCard'
import { ExtensionsCard } from './ExtensionsCard'
import { BackupsCard } from './BackupsCard'
import { SpellCheckCard } from './SpellCheckCard'
import { WatchWordsCard } from './WatchWordsCard'
import { LanguagePacksCard } from './LanguagePacksCard'
import { SceneStagesCard } from './SceneStagesCard'
import { ManuscriptPropertiesCard } from './ManuscriptPropertiesCard'
import { SceneLabelsCard } from './SceneLabelsCard'
import { GroupsCard } from './GroupsCard'
import { CompletionCard } from './CompletionCard'
import { ThemeTokensCard } from './ThemeTokensCard'
import { SceneTemplatesCard } from './SceneTemplatesCard'
import { TagsCard } from './TagsCard'
import './settings.css'

const QUOTE_LANGUAGES = ['en', 'de-low', 'de-guillemet', 'fr', 'es', 'it', 'pt', 'ru', 'pl', 'cs', 'sk']

/** Common typographic fonts offered as a datalist; the field stays free-text so
 * any installed family can be typed. The first three ship with the app and so
 * render identically everywhere; the rest depend on what the machine has. */
/**
 * Faces people choose for readability rather than for looks.
 *
 * Named because the font box is free text and "type the name of a dyslexia
 * font" is not a discoverable instruction - somebody who would benefit has to
 * already know what to search for. Novalist does not bundle these; the picker
 * offers them and the hint says plainly that the one you pick has to be
 * installed.
 */
const ACCESSIBLE_FONTS = [
  'OpenDyslexic',
  'Atkinson Hyperlegible',
  'Lexend',
  'Comic Sans MS',
  'Verdana',
  'Tahoma'
]

const SYSTEM_FONTS = [
  'Newsreader',
  'Fraunces',
  'Courier Prime',
  'Inter',
  'Times New Roman',
  'Georgia',
  'Garamond',
  'Baskerville',
  'Palatino',
  'Book Antiqua',
  'Cambria',
  'Merriweather',
  'Lora',
  'Arial',
  'Helvetica',
  'Verdana',
  'Calibri',
  'Trebuchet MS',
  'Courier New',
  'Consolas'
]

const PAGE_FORMATS: { code: string; name: string }[] = [
  { code: 'USTrade6x9', name: 'US Trade (6x9)' },
  { code: 'Digest5_5x8_5', name: 'Digest (5.5x8.5)' },
  { code: 'A5', name: 'A5 (5.83x8.27)' },
  { code: 'MassMarket', name: 'Mass Market (4.25x6.87)' },
  { code: 'Custom', name: 'Custom' }
]

// Mirrors Novalist.Desktop BookWidthCalculator so the live preview matches.
const PAGE_FORMAT_WIDTHS: Record<string, number> = {
  USTrade6x9: 4.75,
  Digest5_5x8_5: 4.3,
  A5: 4.63,
  MassMarket: 3.35
}
const MEASURE_SAMPLE =
  'abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ,.;:!?\'-"'

function estimateCharsPerLine(
  format: string,
  customWidth: number | null,
  fontFamily: string,
  fontSize: number
): number {
  const inches =
    format === 'Custom'
      ? customWidth && customWidth > 0
        ? customWidth
        : 4.75
      : (PAGE_FORMAT_WIDTHS[format] ?? 4.75)
  const px = inches * 96
  const ctx = document.createElement('canvas').getContext('2d')
  if (!ctx) return 65
  ctx.font = `${fontSize}px "${fontFamily}"`
  const avg = ctx.measureText(MEASURE_SAMPLE).width / MEASURE_SAMPLE.length
  return avg > 0 ? Math.round(px / avg) : 65
}

/** Opening/closing quote pair per auto-replacement language, for the preview. */
const QUOTE_PREVIEW: Record<string, [string, string]> = {
  en: ['“', '”'],
  'de-low': ['„', '“'],
  'de-guillemet': ['»', '«'],
  fr: ['« ', ' »'],
  es: ['«', '»'],
  it: ['«', '»'],
  pt: ['«', '»'],
  ru: ['«', '»'],
  pl: ['„', '“'],
  cs: ['„', '“'],
  sk: ['„', '“']
}

function autoReplacementPreview(language: string): string {
  const [open, close] = QUOTE_PREVIEW[language] ?? QUOTE_PREVIEW.en
  return `'x' → ${open}x${close}   |   -- → —   |   ... → …`
}

type Scope = 'global' | 'project'

/** Text input with a local draft that commits on blur, so per-keystroke RPC
 * round-trips never fight the caret; syncs to the incoming value when idle. */
function SettingInput({
  value,
  onCommit,
  type,
  placeholder,
  id,
  list
}: {
  value: string
  onCommit(next: string): void
  type?: string
  placeholder?: string
  id?: string
  list?: string
}): React.JSX.Element {
  const [draft, setDraft] = useState(value)
  const focused = useRef(false)
  useEffect(() => {
    if (!focused.current) setDraft(value)
  }, [value])
  return (
    <input
      id={id}
      list={list}
      className="dialog-input"
      type={type}
      placeholder={placeholder}
      value={draft}
      onFocus={() => (focused.current = true)}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => {
        focused.current = false
        if (draft !== value) onCommit(draft)
      }}
    />
  )
}

interface SectionDef {
  key: string
  titleKey: string
  keywords: string[]
  body: React.ReactNode
  /** True when the body is a self-contained card (its own title + styling). */
  standalone?: boolean
}

/**
 * The voices the platform has installed, for the read-aloud picker. They arrive
 * asynchronously on every platform and are simply absent on some, so the list
 * starts empty and fills in - which is also why the picker always offers
 * "match the writing language" as its first option.
 */
function useSpeechVoices(): SpeechSynthesisVoice[] {
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([])
  useEffect(() => {
    if (typeof speechSynthesis === 'undefined') return
    const read = (): void => setVoices(speechSynthesis.getVoices())
    read()
    speechSynthesis.addEventListener('voiceschanged', read)
    return () => speechSynthesis.removeEventListener('voiceschanged', read)
  }, [])
  return voices
}

export function SettingsView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const view = useSettingsStore((s) => s.view)
  const load = useSettingsStore((s) => s.load)
  const update = useSettingsStore((s) => s.update)
  const pinSection = useSettingsStore((s) => s.pinSection)
  const clearSection = useSettingsStore((s) => s.clearSection)
  const updateProjectMeta = useSettingsStore((s) => s.updateProjectMeta)
  // Built-in, folder, and extension themes in one list. Subscribed rather than
  // read once, because contributed themes register after the first render.
  const themes = useThemeCatalog((s) => s.themes)
  const assetDirs = assetDirectories()
  const isLoaded = useProjectStore((s) => s.isLoaded)
  // On mobile, hide sections/controls that only make sense on desktop: physical
  // keyboard shortcuts, store-delivered self-update, extensions (deferred), the
  // GitHub token (Git is external on mobile), desktop file-watching, and the
  // file-manager log-folder reveal (no-op in the iOS sandbox).
  const isMobile = window.novalist.isMobile === true
  const settingsSearch = useShellStore((s) => s.settingsSearch)
  const [search, setSearch] = useState('')
  const sectionRefs = useRef<Record<string, HTMLDivElement | null>>({})
  const voices = useSpeechVoices()

  useEffect(() => {
    if (mainView !== 'settings') return
    void load()
  }, [mainView, load])

  // Consume a one-shot deep-link prefill (e.g. "Extensions" from the backstage
  // drawer) and clear it so it does not re-apply on the next visit.
  useEffect(() => {
    if (mainView === 'settings' && settingsSearch) {
      setSearch(settingsSearch)
      useShellStore.getState().settingsSearch && useShellStore.setState({ settingsSearch: '' })
    }
  }, [mainView, settingsSearch])

  if (!view) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const eff = view.effective
  // The language the prose is in, which is what read-aloud speaks and therefore
  // what the voice has to match.
  const writingLanguage = eff.autoReplacementLanguage || 'en'
  const project = view.project

  /** Whether the open project overrides a section. Read from what is stored, so
   * the switch survives leaving and re-entering Settings. */
  const isOverridden = (section: SettingsSection): boolean =>
    view.overriddenSections?.[section] === true

  /**
   * The per-section Global / This-project switch. Ticking pins the values in
   * effect now to the project, so the override exists from that moment rather
   * than only once a field is edited; unticking drops it and the section falls
   * back to the global values.
   */
  const scopeToggle = (section: SettingsSection): React.JSX.Element | null => {
    if (!view.hasProject) return null
    const overridden = isOverridden(section)
    return (
      <>
        <label className="relationships-toggle settings-scope">
          <input
            type="checkbox"
            checked={overridden}
            onChange={(e) => {
              void (e.target.checked ? pinSection(section) : clearSection(section))
            }}
          />
          {t('settings.scopeProjectOverride')}
        </label>
        <p className="settings-hint settings-scope-hint">
          {t(overridden ? 'settings.scopeEditingProject' : 'settings.scopeEditingGlobal')}
        </p>
      </>
    )
  }

  /** Where an edit in a section is written. With no project open, or with the
   * section not overridden, edits go to the global defaults. */
  const scopeFor = (section: SettingsSection): Scope =>
    view.hasProject && isOverridden(section) ? 'project' : 'global'

  const fontDatalist = (
    <datalist id="settings-fonts">
      {[...SYSTEM_FONTS, ...ACCESSIBLE_FONTS].map((f) => (
        <option key={f} value={f} />
      ))}
    </datalist>
  )

  const sections: SectionDef[] = [
    {
      key: 'appearance',
      titleKey: 'settings.appearance',
      keywords: ['appearance', 'language', 'theme', 'accent', 'color', 'interface'],
      body: (
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
          </div>
          <label className="inspector-label" htmlFor="set-accent">
            {t('settings.accentColor')}
          </label>
          <div className="settings-accent-row">
            <input
              id="set-accent"
              className="dialog-input settings-color"
              type="color"
              value={eff.accentColor ?? '#0e8bdf'}
              onChange={(e) =>
                void update(scopeFor('appearance'), { accentColor: e.target.value })
              }
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
    },
    {
      key: 'editor',
      titleKey: 'settings.editor',
      keywords: ['editor', 'font', 'book', 'width', 'page', 'paragraph', 'spacing', 'typewriter',
        'line height', 'leading', 'letter spacing', 'accessibility', 'dyslexia',
        'read aloud', 'speech', 'voice', 'tts'],
      body: (
        <>
          {scopeToggle('editor')}
          {fontDatalist}
          <label className="inspector-label" htmlFor="set-font">
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
          <input
            id="set-fontsize"
            className="dialog-input"
            type="number"
            min={8}
            max={36}
            value={eff.editorFontSize}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                editorFontSize: Math.min(36, Math.max(8, Number(e.target.value)))
              })
            }
          />
          <label className="inspector-label" htmlFor="set-lineheight">
            {t('settings.lineHeight')}
          </label>
          <input
            id="set-lineheight"
            className="dialog-input"
            type="number"
            min={1}
            max={2.5}
            step={0.05}
            value={eff.editorLineHeight}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                editorLineHeight: Math.min(2.5, Math.max(1, Number(e.target.value)))
              })
            }
          />
          <div className="settings-hint">{t('settings.lineHeightDesc')}</div>
          <label className="inspector-label" htmlFor="set-letterspacing">
            {t('settings.letterSpacing')}
          </label>
          <input
            id="set-letterspacing"
            className="dialog-input"
            type="number"
            min={-1}
            max={4}
            step={0.1}
            value={eff.editorLetterSpacing}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                editorLetterSpacing: Math.min(4, Math.max(-1, Number(e.target.value)))
              })
            }
          />
          <div className="settings-hint">{t('settings.letterSpacingDesc')}</div>
          <label className="inspector-label" htmlFor="set-paraspacing">
            {t('settings.paragraphSpacing')}
          </label>
          <input
            id="set-paraspacing"
            className="dialog-input"
            type="number"
            min={0}
            max={3}
            step={0.05}
            value={eff.editorParagraphSpacing}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                editorParagraphSpacing: Math.min(3, Math.max(0, Number(e.target.value)))
              })
            }
          />
          <div className="settings-hint">{t('settings.paragraphSpacingDesc')}</div>
          <label className="inspector-label" htmlFor="set-readaloud-rate">
            {t('settings.readAloudRate')}
          </label>
          <input
            id="set-readaloud-rate"
            className="dialog-input"
            type="number"
            min={0.5}
            max={2}
            step={0.1}
            value={eff.readAloudRate}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                readAloudRate: Math.min(2, Math.max(0.5, Number(e.target.value)))
              })
            }
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
            {voices.map((v) => (
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
          {voices.length === 0 ? (
            <div className="settings-hint export-warning">{t('settings.readAloudNoVoices')}</div>
          ) : (
            !voices.some((v) =>
              v.lang.toLowerCase().startsWith(writingLanguage.slice(0, 2).toLowerCase())
            ) && (
              <div className="settings-hint export-warning">
                {t('settings.readAloudNoMatchingVoice', { language: writingLanguage })}
              </div>
            )
          )}
          <div className="settings-hint">{t('settings.readAloudVoiceKinds')}</div>
          {/* Typewriter scroll makes no sense on a phone (and is force-disabled in
              the mobile editor), so hide it there. */}
          {!isMobile && (
            <>
              <label className="relationships-toggle">
                <input
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
          )}
          <label className="relationships-toggle">
            <input
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
              type="checkbox"
              checked={eff.enableBookParagraphSpacing}
              onChange={(e) =>
                void update(scopeFor('editor'), { enableBookParagraphSpacing: e.target.checked })
              }
            />
            {t('settings.bookSpacing')}
          </label>

          {/* Book width targets a wide desktop editor; it has no effect on a
              phone's narrow column, so hide it on mobile. */}
          {!isMobile && (
            <>
          <label className="relationships-toggle">
            <input
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
                  <input
                    id="set-customwidth"
                    className="dialog-input"
                    type="number"
                    min={1}
                    max={12}
                    step={0.05}
                    value={eff.bookTextBlockWidth ?? 4.75}
                    onChange={(e) =>
                      void update(scopeFor('editor'), {
                        bookTextBlockWidth: Number(e.target.value)
                      })
                    }
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
              <input
                id="set-bookfontsize"
                className="dialog-input"
                type="number"
                min={6}
                max={24}
                value={eff.bookFontSize}
                onChange={(e) =>
                  void update(scopeFor('editor'), {
                    bookFontSize: Math.min(24, Math.max(6, Number(e.target.value)))
                  })
                }
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
          )}
        </>
      )
    },
    {
      key: 'accessibility',
      titleKey: 'settings.accessibility',
      keywords: [
        'accessibility',
        'dyslexia',
        'dyslexic',
        'contrast',
        'spacing',
        'legible',
        'readable',
        'font'
      ],
      body: (
        <>
          <div className="settings-desc">{t('settings.accessibilityDesc')}</div>

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
          <input
            id="set-a11y-size"
            className="dialog-input"
            type="number"
            min={8}
            max={36}
            value={eff.editorFontSize}
            onChange={(e) =>
              void update(scopeFor('editor'), { editorFontSize: Number(e.target.value) })
            }
          />

          <label className="inspector-label" htmlFor="set-a11y-spacing">
            {t('settings.lineHeight')}
          </label>
          <input
            id="set-a11y-spacing"
            className="dialog-input"
            type="number"
            min={1}
            max={3}
            step={0.1}
            value={eff.editorLineHeight}
            onChange={(e) =>
              void update(scopeFor('editor'), {
                editorLineHeight: Math.min(2.5, Math.max(1, Number(e.target.value)))
              })
            }
          />
          <div className="settings-hint">{t('settings.lineHeightDesc')}</div>

          {/* One click rather than a paragraph telling somebody where the
              theme picker is. */}
          <button
            className="dialog-button"
            onClick={() => void update('global', { theme: 'High Contrast' })}
          >
            {t('settings.useHighContrast')}
          </button>
          <div className="settings-hint">{t('settings.highContrastHint')}</div>
        </>
      )
    },
    {
      key: 'writingGoals',
      titleKey: 'settings.writingGoals',
      keywords: [
        'goal',
        'deadline',
        'author',
        'writing',
        'target',
        'targets',
        'words',
        'chapter',
        'scene',
        'act'
      ],
      body: (
        <>
          <div className="settings-desc">{t('settings.goalsDesc')}</div>
          {view.hasProject && project ? (
            <>
              <label className="inspector-label" htmlFor="set-deadline">
                {t('settings.projectDeadline')}
              </label>
              <input
                id="set-deadline"
                className="dialog-input"
                type="date"
                value={project.deadline ?? ''}
                onChange={(e) => void updateProjectMeta({ deadline: e.target.value })}
              />
              <label className="inspector-label" htmlFor="set-author">
                {t('settings.projectAuthor')}
              </label>
              <SettingInput
                id="set-author"
                value={project.author}
                onCommit={(v) => void updateProjectMeta({ author: v })}
              />

              <label className="inspector-label" htmlFor="set-daily-goal">
                {t('settings.dailyWordGoal')}
              </label>
              <SettingInput
                id="set-daily-goal"
                value={String(project.dailyGoal)}
                onCommit={(v) => void updateProjectMeta({ dailyGoal: Number(v) || 0 })}
              />
              <div className="settings-hint">{t('settings.dailyWordGoalDesc')}</div>

              {/* Longer horizons. Blank or zero turns one off, which is what
                  every project starts at - nobody is given a weekly budget
                  they did not ask for. */}
              <label className="inspector-label" htmlFor="set-weekly-goal">
                {t('settings.weeklyWordGoal')}
              </label>
              <SettingInput
                id="set-weekly-goal"
                value={String(project.weeklyGoal || '')}
                onCommit={(v) => void updateProjectMeta({ weeklyGoal: Number(v) || 0 })}
              />
              <div className="settings-hint">{t('settings.weeklyWordGoalDesc')}</div>

              <label className="inspector-label" htmlFor="set-monthly-goal">
                {t('settings.monthlyWordGoal')}
              </label>
              <SettingInput
                id="set-monthly-goal"
                value={String(project.monthlyGoal || '')}
                onCommit={(v) => void updateProjectMeta({ monthlyGoal: Number(v) || 0 })}
              />
              <div className="settings-hint">{t('settings.monthlyWordGoalDesc')}</div>

              <label className="inspector-label" htmlFor="set-project-goal">
                {t('settings.projectWordGoal')}
              </label>
              <SettingInput
                id="set-project-goal"
                value={String(project.projectGoal)}
                onCommit={(v) => void updateProjectMeta({ projectGoal: Number(v) || 0 })}
              />
              <div className="settings-hint">{t('settings.projectWordGoalDesc')}</div>

              {/* Per-act, per-chapter and per-scene targets. The same panel the
                  Dashboard shows, because this is where writers look for it. */}
              <label className="inspector-label">{t('targets.dashboardTitle')}</label>
              <TargetsPanel />
            </>
          ) : (
            <div className="settings-hint">{t('settings.scopeProjectHint')}</div>
          )}
        </>
      )
    },
    {
      key: 'writingAssistance',
      titleKey: 'settings.writingAssistance',
      keywords: ['auto', 'replacement', 'quote', 'dialogue', 'grammar', 'spelling'],
      body: (
        <>
          {scopeToggle('writing')}
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
            {QUOTE_LANGUAGES.map((lang) => (
              <option key={lang} value={lang}>
                {lang}
              </option>
            ))}
          </select>
          <div className="settings-preview">
            {t('settings.preview')}: {autoReplacementPreview(eff.autoReplacementLanguage)}
          </div>
          <label className="inspector-label" htmlFor="set-reviewer">
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
            languages={eff.spellCheckLanguages}
            onLanguagesChange={(languages) =>
              void update(scopeFor('writing'), { spellCheckLanguages: languages })
            }
          />
          {/* Words the writer wants counted, beside the checks Novalist
              brings. Their habits follow them, so this is a global list. */}
          <WatchWordsCard />
          <label className="relationships-toggle">
            <input
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
              <label className="inspector-label" htmlFor="set-gc-url">
                {t('settings.grammarCheckApiUrl')}
              </label>
              <SettingInput
                id="set-gc-url"
                value={eff.grammarCheckApiUrl ?? ''}
                placeholder="https://api.languagetool.org/v2/check"
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
              />
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
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={eff.grammarCheckPickyMode}
                  onChange={(e) =>
                    void update(scopeFor('writing'), { grammarCheckPickyMode: e.target.checked })
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
                {['en', 'de', 'fr', 'es', 'it', 'pt', 'nl', 'pl', 'ru', 'zh', 'ja'].map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </select>
            </div>
          )}
        </>
      )
    },
    ...(isLoaded
      ? [
          {
            key: 'templates',
            titleKey: 'settings.templates',
            keywords: ['template', 'character', 'location', 'item', 'lore'],
            body: <TemplatesCard />,
            standalone: true
          }
        ]
      : []),
    {
      key: 'hotkeys',
      titleKey: 'settings.hotkeys',
      keywords: ['hotkey', 'keyboard', 'shortcut', 'key', 'binding', 'gesture'],
      body: <HotkeysCard />,
      standalone: true
    },
    {
      key: 'updatesIntegrations',
      titleKey: 'settings.updatesIntegrations',
      keywords: ['update', 'extension', 'github', 'token', 'pat', 'integration', 'general'],
      body: (
        <>
          {/* App self-update is disabled on the Mac App Store build (the store
              delivers updates), so hide the toggle there. */}
          {!window.novalist.isMas && (
            <>
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={Boolean(view.global.checkForUpdates)}
                  onChange={(e) => void update('global', { checkForUpdates: e.target.checked })}
                />
                {t('update.checkForUpdates')}
              </label>
              <div className="settings-hint">{t('update.checkForUpdatesDesc')}</div>
            </>
          )}
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={Boolean(view.global.checkForExtensionUpdates)}
              onChange={(e) =>
                void update('global', { checkForExtensionUpdates: e.target.checked })
              }
            />
            {t('settings.checkForExtensionUpdates')}
          </label>
          <div className="settings-hint">{t('settings.checkForExtensionUpdatesDesc')}</div>
          <label className="inspector-label" htmlFor="set-github-token">
            {t('settings.githubToken')}
          </label>
          <SettingInput
            id="set-github-token"
            type="password"
            value={String(view.global.gitHubToken ?? '')}
            onCommit={(v) => void update('global', { gitHubToken: v.trim() || null })}
          />
          <div className="settings-hint">{t('settings.githubTokenDesc')}</div>
        </>
      )
    },
    {
      key: 'backups',
      titleKey: 'backup.title',
      keywords: ['backup', 'archive', 'restore', 'zip', 'recovery', 'safety'],
      body: <BackupsCard />
    },
    {
      key: 'sceneStages',
      titleKey: 'stages.title',
      keywords: ['stage', 'status', 'revision', 'draft', 'progress', 'scene'],
      body: <SceneStagesCard />
    },
    {
      key: 'sceneLabels',
      titleKey: 'labels.title',
      keywords: ['label', 'labels', 'colour', 'color', 'flag', 'scene', 'corkboard'],
      body: <SceneLabelsCard />
    },
    {
      key: 'themeTokens',
      titleKey: 'themeTokens.title',
      keywords: ['token', 'tokens', 'theme', 'colour', 'color', 'appearance', 'font', 'radius', 'spacing'],
      body: <ThemeTokensCard />
    },
    {
      key: 'completion',
      titleKey: 'completion.title',
      keywords: ['completion', 'autocomplete', 'words', 'phrases', 'vocabulary', 'typing', 'spelling'],
      body: <CompletionCard />
    },
    {
      key: 'groups',
      titleKey: 'groups.title',
      keywords: ['group', 'groups', 'faction', 'factions', 'house', 'crew', 'family', 'colour', 'color'],
      body: <GroupsCard />
    },
    {
      key: 'sceneTemplates',
      titleKey: 'sceneTemplates.title',
      keywords: ['template', 'templates', 'scene', 'preset', 'skeleton', 'start'],
      body: <SceneTemplatesCard />
    },
    {
      key: 'tags',
      titleKey: 'tags.title',
      keywords: ['tag', 'tags', 'label', 'colour', 'color', 'merge', 'rename', 'vocabulary'],
      body: <TagsCard />
    },
    {
      key: 'manuscriptProperties',
      titleKey: 'props.title',
      keywords: [
        'property',
        'properties',
        'field',
        'fields',
        'custom',
        'metadata',
        'column',
        'scene',
        'chapter',
        'tension'
      ],
      body: <ManuscriptPropertiesCard />
    },
    {
      key: 'languagePacks',
      titleKey: 'languagePacks.title',
      keywords: ['language', 'locale', 'translation', 'lexicon', 'analysis', 'i18n', 'pack'],
      body: <LanguagePacksCard />
    },
    {
      key: 'diagnostics',
      titleKey: 'settings.diagnostics',
      keywords: ['log', 'logging', 'diagnostic', 'support'],
      body: (
        <>
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={Boolean(view.global.diagnosticLoggingEnabled)}
              onChange={(e) =>
                void update('global', { diagnosticLoggingEnabled: e.target.checked })
              }
            />
            {t('settings.diagnosticLogging')}
          </label>
          <div className="settings-hint">{t('settings.diagnosticLoggingDesc')}</div>
          <div className="settings-button-row">
            {/* Revealing / opening the log in a file manager has no iOS equivalent
                (revealPath is a no-op in the sandbox), so hide those on mobile and
                keep only Clear logs. A "share log" action is a later addition. */}
            {!isMobile && (
              <>
                <button
                  className="dialog-button"
                  onClick={() => {
                    void (async () => {
                      const info = await rpc.request<{
                        directory: string
                        currentLog: string | null
                      }>('settings/logInfo')
                      await window.novalist.revealPath(info.directory)
                    })()
                  }}
                >
                  {t('settings.openLogFolder')}
                </button>
                <button
                  className="dialog-button"
                  onClick={() => {
                    void (async () => {
                      const info = await rpc.request<{
                        directory: string
                        currentLog: string | null
                      }>('settings/logInfo')
                      if (info.currentLog) await window.novalist.openExternal(info.currentLog)
                      else await window.novalist.revealPath(info.directory)
                    })()
                  }}
                >
                  {t('settings.openCurrentLog')}
                </button>
              </>
            )}
            <button
              className="dialog-button"
              onClick={() => void rpc.request('settings/clearLogs')}
            >
              {t('settings.clearLogs')}
            </button>
          </div>
        </>
      )
    },
    {
      key: 'extensions',
      titleKey: 'extensions.title',
      keywords: ['extension', 'plugin', 'addon'],
      body: <ExtensionsCard />,
      standalone: true
    }
  ]

  // Whole sections that only make sense on desktop. Hotkeys need a physical
  // keyboard; Updates/Integrations covers store-delivered self-update, extension
  // updates, and the GitHub token (Git is external on mobile); Extensions are
  // deferred on mobile (App Store remote-code rules).
  const HIDDEN_ON_MOBILE = new Set(['hotkeys', 'updatesIntegrations', 'extensions'])
  const visibleSections = isMobile
    ? sections.filter((s) => !HIDDEN_ON_MOBILE.has(s.key))
    : sections

  const query = search.trim().toLowerCase()
  const sectionVisible = (s: SectionDef): boolean =>
    query.length === 0 ||
    t(s.titleKey).toLowerCase().includes(query) ||
    s.keywords.some((k) => k.includes(query))

  const jumpTo = (key: string): void => {
    setSearch('')
    requestAnimationFrame(() => {
      sectionRefs.current[key]?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  }

  return (
    <div className="dashboard settings-view">
      <div className="settings-header">
        <h1 className="dashboard-title">{t('settings.title')}</h1>
        <input
          className="dialog-input settings-search"
          placeholder={t('settings.searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>

      <div className="settings-layout">
        <nav className="settings-nav">
          {visibleSections.map((s) => (
            <button key={s.key} className="settings-nav-item" onClick={() => jumpTo(s.key)}>
              {t(s.titleKey)}
            </button>
          ))}
        </nav>

        <div className="settings-sections">
          {visibleSections.map((s) =>
            sectionVisible(s) ? (
              s.standalone ? (
                <div
                  key={s.key}
                  className="settings-anchor"
                  ref={(el) => {
                    sectionRefs.current[s.key] = el
                  }}
                >
                  {s.body}
                </div>
              ) : (
                <div
                  key={s.key}
                  className="dashboard-card export-card"
                  ref={(el) => {
                    sectionRefs.current[s.key] = el
                  }}
                >
                  <div className="dashboard-card-title">{t(s.titleKey)}</div>
                  {s.body}
                </div>
              )
            ) : null
          )}
        </div>
      </div>
    </div>
  )
}
