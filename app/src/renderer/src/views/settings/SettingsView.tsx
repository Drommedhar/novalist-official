import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import ReactMarkdown from 'react-markdown'
import { ArrowLeft, ChevronLeft, ExternalLink, FolderOpen, Search, X } from 'lucide-react'
import { availableLanguages } from '../../i18n'
import { rpc } from '../../rpc/client'
import { useSettingsStore, type SettingsSection } from '../../stores/settingsStore'
import {
  DEFAULT_UI_SCALE,
  UI_SCALE_STEPS,
  useUiScaleStore
} from '../../stores/uiScaleStore'
import { useOnboardingStore } from '../../stores/onboardingStore'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'
import { MobileGroup, MobileNav, MobileRow, useMobileNav } from '../../shell/MobileNav'
import { useIsPhone } from '../../shell/useIsPhone'
import { TargetsPanel } from '../dashboard/TargetsCard'
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
import { AutoReplacementsCard } from './AutoReplacementsCard'
import {
  SETTINGS_CATEGORIES,
  searchSettings,
  settingsControl,
  settingsSectionsForContext,
  type SettingsControlMetadata,
  type SettingsScopeKind,
  type SettingsSectionKey,
  type SettingsSectionMetadata
} from './settingsRegistry'
import {
  parseSettingsDestination,
  setSettingsDestination,
  useSettingsNavigation
} from './settingsNavigation'
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

/**
 * Number field that lets the writer finish typing before it judges the number.
 *
 * These settings clamp, and clamping on every keystroke makes most values
 * impossible to reach: with a minimum of 8, the "2" of "22" is read as 2,
 * pushed up to 8, and written back into the box before the second digit
 * arrives - so font size went 17, 8, 36 and never 22. A decimal fares worse,
 * because the box is empty for as long as it takes to type the separator.
 *
 * On desktop the stepper arrows hid it. An iPhone has no steppers and the
 * keyboard is the only way in, which is where "the font size cannot be
 * changed" came from.
 *
 * The box is left uncontrolled while it has focus - React writing a normalized
 * value back mid-word is exactly what ate the keystrokes - and the value is
 * read, clamped and committed once, when focus leaves. An empty or unreadable
 * box means "leave it alone", not "zero".
 */
function SettingNumber({
  id,
  value,
  min,
  max,
  step,
  onCommit
}: {
  id: string
  value: number
  min: number
  max: number
  step?: number
  onCommit(next: number): void
}): React.JSX.Element {
  const ref = useRef<HTMLInputElement>(null)
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current && ref.current) ref.current.value = String(value)
  }, [value])

  const commit = (): void => {
    focused.current = false
    const raw = ref.current?.value ?? ''
    const parsed = Number(raw)
    const next =
      raw.trim() === '' || Number.isNaN(parsed) ? value : Math.min(max, Math.max(min, parsed))
    if (ref.current) ref.current.value = String(next)
    if (next !== value) onCommit(next)
  }

  return (
    <input
      ref={ref}
      id={id}
      className="dialog-input"
      type="number"
      min={min}
      max={max}
      step={step}
      defaultValue={String(value)}
      onFocus={() => (focused.current = true)}
      onBlur={commit}
      // A phone's number pad has no return key; the keyboard's Done button
      // blurs and commits. On a desktop keyboard Enter should not have to be a
      // click somewhere else.
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur()
      }}
    />
  )
}

interface SectionBodyDef {
  key: SettingsSectionKey
  body: React.ReactNode
  /** True when the body is a self-contained card (its own title + styling). */
  standalone?: boolean
}

type ResolvedSection = SettingsSectionMetadata & SectionBodyDef

interface DisplayDiagnostics {
  zoomFactor: number
  scaleFactor: number
  windowBounds: { width: number; height: number }
  contentBounds: { width: number; height: number }
  workArea: { width: number; height: number }
}

/** A section as one row of the phone index: tapping it pushes the section. */
function SettingsPhoneRow({ section }: { section: ResolvedSection }): React.JSX.Element {
  const { t } = useTranslation()
  const nav = useMobileNav()
  const title = t(section.titleKey)
  return (
    <MobileRow
      label={title}
      onClick={() => nav.push({ id: section.key, title })}
    />
  )
}

function controlTarget(
  container: HTMLElement,
  metadata: SettingsControlMetadata,
  translatedLabel: string
): HTMLElement | null {
  if (metadata.targetId) {
    const exact = document.getElementById(metadata.targetId)
    if (exact && container.contains(exact)) return exact
  }

  const wanted = translatedLabel.trim().toLocaleLowerCase()
  const labelled = [...container.querySelectorAll<HTMLElement>('label, button, summary')].find(
    (candidate) => candidate.textContent?.trim().toLocaleLowerCase().includes(wanted)
  )
  if (!labelled) return null
  if (labelled instanceof HTMLLabelElement && labelled.htmlFor) {
    return document.getElementById(labelled.htmlFor) ?? labelled
  }
  return labelled.querySelector<HTMLElement>('input, select, textarea, button') ?? labelled
}

function scopeLabelKey(
  scope: SettingsScopeKind,
  projectOverride: boolean
): string {
  if (scope === 'project') return 'settings.scopeProjectBadge'
  if (scope === 'mixed') return 'settings.scopeMixedBadge'
  if (scope === 'overridable') {
    return projectOverride ? 'settings.scopeOverrideProjectBadge' : 'settings.scopeOverrideGlobalBadge'
  }
  return 'settings.scopeGlobalBadge'
}

/**
 * The voices the platform has installed, for the read-aloud picker. They arrive
 * asynchronously on every platform and are simply absent on some, so the list
 * starts empty and fills in - which is also why the picker always offers
 * "match the writing language" as its first option.
 */
/**
 * The voices the system engine offers, via the backend.
 *
 * The browser's own list is a subset on Windows: it reads one voice store while
 * everything a writer installs to get more voices registers in the other. A
 * machine offering every other application three hundred voices offered
 * Novalist three, and no setting could change it.
 */
function useSystemVoices(): { id: string; name: string; language: string }[] {
  const [voices, setVoices] = useState<{ id: string; name: string; language: string }[]>([])
  useEffect(() => {
    void rpc
      .request<{ id: string; name: string; language: string }[]>('voices/list')
      .then(setVoices)
      .catch(() => setVoices([]))
  }, [])
  return voices
}

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
  // On mobile, hide sections/controls that only make sense on desktop: physical
  // keyboard shortcuts, store-delivered self-update, extensions (deferred), the
  // GitHub token (Git is external on mobile), desktop file-watching, and the
  // file-manager log-folder reveal (no-op in the iOS sandbox).
  const isMobile = window.novalist.isMobile === true
  const isPhone = useIsPhone()
  const projectLoaded = useProjectStore((s) => s.isLoaded)
  const projectName = useProjectStore((s) => s.projectName)
  const closeProject = useProjectStore((s) => s.closeProject)
  const settingsSearch = useShellStore((s) => s.settingsSearch)
  const setMainView = useShellStore((s) => s.setMainView)
  const uiScale = useUiScaleStore((s) => s.percent)
  const setUiScale = useUiScaleStore((s) => s.setPercent)
  const resetUiScale = useUiScaleStore((s) => s.reset)
  const tipsEnabled = useOnboardingStore((s) => s.tipsEnabled)
  const setTipsEnabled = useOnboardingStore((s) => s.setTipsEnabled)
  const destination = useSettingsNavigation((s) => s.destination)
  const destinationRevision = useSettingsNavigation((s) => s.revision)
  const [search, setSearch] = useState(destination.query ?? '')
  const [selectedSection, setSelectedSection] = useState<SettingsSectionKey>(
    destination.section ?? 'appearance'
  )
  const [displayInfo, setDisplayInfo] = useState<DisplayDiagnostics | null>(null)
  const [displayInfoBusy, setDisplayInfoBusy] = useState(false)
  const sectionSurfaceRef = useRef<HTMLDivElement>(null)
  const voices = useSpeechVoices()
  const systemVoices = useSystemVoices()

  /**
   * Close the project from inside Settings (mobile only - see the registry's
   * 'project' section).
   *
   * The screen has to be left as well as the project: with nothing open the
   * mobile frame shows the welcome content unless mainView is one of the
   * app-scoped views, and mainView is 'settings' precisely because that is
   * where this button is. Without the reset the writer presses "Close project"
   * and stays looking at Settings. The tab goes back to the first one too, so
   * the next project opens on its dashboard rather than back in here.
   */
  const closeProjectFromSettings = async (): Promise<void> => {
    await closeProject()
    useShellStore.getState().setMobileTab('dashboard')
    setMainView('dashboard')
  }

  const refreshDisplayInfo = async (): Promise<void> => {
    if (!window.novalist.displayDiagnostics) return
    setDisplayInfoBusy(true)
    try {
      setDisplayInfo(await window.novalist.displayDiagnostics())
    } finally {
      setDisplayInfoBusy(false)
    }
  }

  const availableMetadata = useMemo(
    () =>
      settingsSectionsForContext({
        hasProject: view?.hasProject === true,
        isMobile
      }),
    [isMobile, view?.hasProject]
  )

  const searchResults = useMemo(
    () => searchSettings(availableMetadata, search, (key) => t(key)),
    [availableMetadata, search, t]
  )

  useEffect(() => {
    void load()
  }, [load])

  // Keep the old search-string bridge working until every shell caller uses the
  // typed Settings-owned destination API. A settings/section/control string is
  // already a precise route; other text remains a translated search query.
  useEffect(() => {
    if (settingsSearch) {
      const parsed = parseSettingsDestination(settingsSearch)
      if (parsed) {
        setSettingsDestination({ ...parsed, origin: destination.origin })
      } else {
        setSearch(settingsSearch)
      }
      useShellStore.getState().settingsSearch && useShellStore.setState({ settingsSearch: '' })
    }
  }, [destination.origin, settingsSearch])

  useEffect(() => {
    if (destination.query !== undefined) setSearch(destination.query)
    if (destination.section) setSelectedSection(destination.section)
  }, [destination, destinationRevision])

  // A project-only destination can outlive the project it came from. Fall back
  // to the first available global section instead of leaving an empty surface.
  useEffect(() => {
    if (availableMetadata.some((section) => section.key === selectedSection)) return
    const fallback = availableMetadata[0]?.key
    if (!fallback) return
    setSelectedSection(fallback)
    setSettingsDestination({ section: fallback, origin: destination.origin })
  }, [availableMetadata, destination.origin, selectedSection])

  // Focus the exact control after its one section has mounted. For older
  // self-contained cards without stable ids, controlTarget falls back to the
  // translated label rather than making the route depend on English text.
  useEffect(() => {
    if (!view || !destination.control || destination.section !== selectedSection) return
    const metadata = settingsControl(selectedSection, destination.control)
    const surface = sectionSurfaceRef.current
    if (!metadata || !surface) return

    let target: HTMLElement | null = null
    const frame = requestAnimationFrame(() => {
      target = controlTarget(surface, metadata, t(metadata.labelKey)) ?? surface
      target.scrollIntoView({ behavior: 'smooth', block: 'center' })
      if (target.matches('input, select, textarea, button, summary, [tabindex]')) {
        target.focus({ preventScroll: true })
      }
      target.classList.add('settings-deep-link-target')
    })
    return () => {
      cancelAnimationFrame(frame)
      target?.classList.remove('settings-deep-link-target')
    }
  }, [destination.control, destination.section, destinationRevision, selectedSection, t, view])

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

  const sectionBodies: SectionBodyDef[] = [
    {
      /* Mobile only (see the registry): the way out of a project, which on
         desktop is a menu item and a palette command instead. */
      key: 'project',
      body: (
        <>
          {projectName && <p className="settings-open-project">{projectName}</p>}
          <p className="settings-hint">{t('settings.closeProjectDesc')}</p>
          <button className="dialog-button" onClick={() => void closeProjectFromSettings()}>
            {t('command.closeProject')}
          </button>
        </>
      )
    },
    {
      key: 'appearance',
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
          <label className="inspector-label" htmlFor="set-ui-scale">
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
          <div className="settings-hint">{t('settings.lineHeightDesc')}</div>
          <label className="inspector-label" htmlFor="set-letterspacing">
            {t('settings.letterSpacing')}
          </label>
          <SettingNumber
            id="set-letterspacing"
            min={-1}
            max={4}
            step={0.1}
            value={eff.editorLetterSpacing}
            onCommit={(v) => void update(scopeFor('editor'), { editorLetterSpacing: v })}
          />
          <div className="settings-hint">{t('settings.letterSpacingDesc')}</div>
          <label className="inspector-label" htmlFor="set-paraspacing">
            {t('settings.paragraphSpacing')}
          </label>
          <SettingNumber
            id="set-paraspacing"
            min={0}
            max={3}
            step={0.05}
            value={eff.editorParagraphSpacing}
            onCommit={(v) => void update(scopeFor('editor'), { editorParagraphSpacing: v })}
          />
          <div className="settings-hint">{t('settings.paragraphSpacingDesc')}</div>
          <label className="inspector-label" htmlFor="set-readaloud-rate">
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
          )}
          {/* Typewriter scroll makes no sense on a phone (and is force-disabled in
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
          )}
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

          {/* Book width targets a wide desktop editor; it has no effect on a
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
          )}
        </>
      )
    },
    {
      key: 'accessibility',
      body: (
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
    },
    {
      key: 'writingGoals',
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

              {/* Per project: a trade paperback, a mass-market and a large-print
                  edition are three different answers, and a writer working on
                  two of them at once needs two. */}
              <label className="inspector-label" htmlFor="set-words-per-page">
                {t('settings.wordsPerPage')}
              </label>
              <SettingInput
                id="set-words-per-page"
                value={String(project.wordsPerPage)}
                onCommit={(v) => void updateProjectMeta({ wordsPerPage: Number(v) || 0 })}
              />
              <div className="settings-hint">{t('settings.wordsPerPageDesc')}</div>

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
      body: (
        <>
          {scopeToggle('writing')}
          {/* The master switch comes before the style it governs. Off, nothing
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
            {QUOTE_LANGUAGES.map((lang) => (
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
          <AutoReplacementsCard scope={scopeFor('writing')} />
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
                  id="set-gc-picky"
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
    {
      key: 'templates',
      body: <TemplatesCard />,
      standalone: true
    },
    {
      key: 'hotkeys',
      body: <HotkeysCard />,
      standalone: true
    },
    {
      key: 'updatesIntegrations',
      body: (
        <>
          {/* App self-update is disabled on the Mac App Store build (the store
              delivers updates), so hide the toggle there. */}
          {!window.novalist.isMas && (
            <>
              <label className="relationships-toggle">
                <input
                  id="set-check-updates"
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
              id="set-extension-updates"
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
      body: <BackupsCard />
    },
    {
      key: 'sceneStages',
      body: <SceneStagesCard />
    },
    {
      key: 'sceneLabels',
      body: <SceneLabelsCard />
    },
    {
      key: 'themeTokens',
      body: <ThemeTokensCard />
    },
    {
      key: 'completion',
      body: <CompletionCard />
    },
    {
      key: 'groups',
      body: <GroupsCard />
    },
    {
      key: 'sceneTemplates',
      body: <SceneTemplatesCard />
    },
    {
      key: 'tags',
      body: <TagsCard />
    },
    {
      key: 'manuscriptProperties',
      body: <ManuscriptPropertiesCard />
    },
    {
      key: 'languagePacks',
      body: <LanguagePacksCard />
    },
    {
      key: 'diagnostics',
      body: (
        <>
          {!isMobile && window.novalist.displayDiagnostics && (
            <div className="settings-display-diagnostics">
              <div className="settings-desc">{t('settings.displayInfoDesc')}</div>
              <button
                id="set-display-diagnostics"
                className="dialog-button"
                disabled={displayInfoBusy}
                onClick={() => void refreshDisplayInfo()}
              >
                {t(displayInfoBusy ? 'settings.displayInfoReading' : 'settings.displayInfoRefresh')}
              </button>
              {displayInfo && (
                <dl
                  className="settings-display-grid"
                  data-testid="display-diagnostics"
                  data-zoom-factor={displayInfo.zoomFactor}
                  data-scale-factor={displayInfo.scaleFactor}
                >
                  <div>
                    <dt>{t('settings.uiScale')}</dt>
                    <dd>{Math.round(displayInfo.zoomFactor * 100)}%</dd>
                  </div>
                  <div>
                    <dt>{t('settings.osScale')}</dt>
                    <dd>{Math.round(displayInfo.scaleFactor * 100)}%</dd>
                  </div>
                  <div>
                    <dt>{t('settings.windowSize')}</dt>
                    <dd>{displayInfo.windowBounds.width} × {displayInfo.windowBounds.height}</dd>
                  </div>
                  <div>
                    <dt>{t('settings.contentSize')}</dt>
                    <dd>{displayInfo.contentBounds.width} × {displayInfo.contentBounds.height}</dd>
                  </div>
                  <div>
                    <dt>{t('settings.workAreaSize')}</dt>
                    <dd>{displayInfo.workArea.width} × {displayInfo.workArea.height}</dd>
                  </div>
                </dl>
              )}
            </div>
          )}
          <label className="relationships-toggle">
            <input
              id="set-diagnostic-logging"
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
      body: <ExtensionsCard />,
      standalone: true
    }
  ]

  const bodiesByKey = new Map(sectionBodies.map((section) => [section.key, section]))
  const visibleSections: ResolvedSection[] = availableMetadata.flatMap((metadata) => {
    const body = bodiesByKey.get(metadata.key)
    return body ? [{ ...metadata, ...body, standalone: metadata.standalone ?? body.standalone }] : []
  })
  const activeSection =
    visibleSections.find((section) => section.key === selectedSection) ?? visibleSections[0]
  const query = search.trim()

  const goTo = (section: SettingsSectionKey, control?: string): void => {
    setSearch('')
    setSelectedSection(section)
    setSettingsDestination({ section, control, origin: destination.origin })
  }

  const sectionOverride = (section: SettingsSectionKey): SettingsSection | null => {
    if (section === 'appearance' || section === 'editor') return section
    if (section === 'writingAssistance') return 'writing'
    return null
  }

  const overrideActive = (section: SettingsSectionMetadata): boolean => {
    const override = sectionOverride(section.key)
    return override ? isOverridden(override) : false
  }

  const returnToOrigin = (): void => {
    if (!destination.origin) return
    const origin = destination.origin
    setSettingsDestination({ section: activeSection?.key ?? 'appearance' })
    setMainView(origin.view)
  }

  // Phone navigation keeps the native-style drilldown, but its groups and
  // availability now come from the same registry as desktop.
  if (isPhone) {
    const resultKeys = new Set(searchResults.map((result) => result.section.key))
    const shown = query
      ? visibleSections.filter((section) => resultKeys.has(section.key))
      : visibleSections
    const grouped = SETTINGS_CATEGORIES.map((category) => ({
      id: category,
      // The open project is a group of its own at the top of the index rather
      // than a page to drill into - closing is one press, not a row that opens
      // a screen holding one button. Under a search it stays an ordinary
      // result, so "close" still finds it.
      sections: shown.filter(
        (section) => section.category === category && section.key !== 'project'
      )
    })).filter((group) => group.sections.length > 0)

    return (
      <MobileNav
        title={t('settings.title')}
        // Resolved out of this render's sections, so an open section shows the
        // value the setting has now. The search filter is deliberately not
        // consulted: typing in the index must not blank the page on top of it.
        renderPage={(key) => {
          const section = visibleSections.find((candidate) => candidate.key === key)
          return section ? <div className="settings-phone-section">{section.body}</div> : null
        }}
      >
        <div className="settings-phone">
          {/* Settings is the one screen a writer can open before a project, and
              on a phone there is nothing around it then - no rail, and the
              native tab bar stays hidden until a project is open. Without this
              the welcome screen was a one-way door. Inside a project the tab
              bar is the way out, so the button is not there. */}
          {!projectLoaded && (
            <button
              type="button"
              className="mobile-nav-back settings-phone-back"
              onClick={() => setMainView('dashboard')}
            >
              <ChevronLeft size={20} strokeWidth={2} />
              <span className="mobile-nav-back-label">Novalist</span>
            </button>
          )}
          <h1 className="mobile-nav-title settings-phone-title">{t('settings.title')}</h1>
          <input
            className="dialog-input settings-phone-search"
            placeholder={t('settings.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {/* The project, and the way back out of it. iOS states what is open
              and offers the one action on it; there is no other way to leave a
              project on a phone - no menu bar, and the tab bar only moves
              between screens inside it. */}
          {!query && projectLoaded && (
            <MobileGroup header={projectName ?? undefined} footer={t('settings.closeProjectDesc')}>
              <MobileRow
                label={t('command.closeProject')}
                variant="action"
                onClick={() => void closeProjectFromSettings()}
              />
            </MobileGroup>
          )}
          {query ? (
            <MobileGroup>
              {shown.map((s) => (
                <SettingsPhoneRow key={s.key} section={s} />
              ))}
            </MobileGroup>
          ) : (
            <>
              {grouped.map((group) => (
                <MobileGroup key={group.id} header={t(`settings.group.${group.id}`)}>
                  {group.sections.map((s) => (
                    <SettingsPhoneRow key={s.key} section={s} />
                  ))}
                </MobileGroup>
              ))}
            </>
          )}
        </div>
      </MobileNav>
    )
  }

  return (
    <div className="dashboard settings-view">
      <div className="settings-header">
        <div className="settings-heading-copy">
          {destination.origin && (
            <button className="settings-origin" onClick={returnToOrigin}>
              <ArrowLeft size={16} aria-hidden="true" />
              {t('settings.backTo', { context: t(destination.origin.labelKey) })}
            </button>
          )}
          <h1 className="dashboard-title">{t('settings.title')}</h1>
        </div>
        <div className="settings-search-box">
          <Search size={16} aria-hidden="true" />
          <input
            className="settings-search"
            type="search"
            aria-label={t('settings.searchPlaceholder')}
            placeholder={t('settings.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {query && (
            <button
              className="settings-search-clear"
              aria-label={t('settings.searchClear')}
              onClick={() => setSearch('')}
            >
              <X size={16} aria-hidden="true" />
            </button>
          )}
        </div>
      </div>

      <div className="settings-layout">
        <nav className="settings-nav" aria-label={t('settings.navigationLabel')}>
          {SETTINGS_CATEGORIES.map((category) => {
            const categorySections = visibleSections.filter(
              (section) => section.category === category
            )
            if (categorySections.length === 0) return null
            return (
              <div className="settings-nav-group" key={category}>
                <div className="settings-nav-heading">{t(`settings.group.${category}`)}</div>
                {categorySections.map((section) => (
                  <button
                    key={section.key}
                    className={`settings-nav-item${
                      activeSection?.key === section.key ? ' active' : ''
                    }`}
                    aria-current={activeSection?.key === section.key ? 'page' : undefined}
                    onClick={() => goTo(section.key)}
                  >
                    {t(section.titleKey)}
                  </button>
                ))}
              </div>
            )
          })}
        </nav>

        <div className="settings-sections">
          {query ? (
            <section className="settings-results" aria-live="polite">
              <h2 className="settings-results-title">
                {t('settings.searchResults', { count: searchResults.length })}
              </h2>
              {searchResults.length === 0 ? (
                <p className="settings-results-empty">{t('settings.searchNoResults')}</p>
              ) : (
                <div className="settings-results-list">
                  {searchResults.map((result) => {
                    const resultKey = `${result.section.key}:${result.control?.key ?? 'section'}`
                    const resultTitle = result.control
                      ? t(result.control.labelKey)
                      : t(result.section.titleKey)
                    return (
                      <button
                        className="settings-result"
                        key={resultKey}
                        onClick={() => goTo(result.section.key, result.control?.key)}
                      >
                        <span className="settings-result-title">{resultTitle}</span>
                        <span className="settings-result-context">
                          {t(result.section.titleKey)} ·{' '}
                          {t(`settings.group.${result.section.category}`)}
                        </span>
                      </button>
                    )
                  })}
                </div>
              )}
            </section>
          ) : activeSection ? (
            <section
              ref={sectionSurfaceRef}
              className="settings-section-surface"
              data-settings-section={activeSection.key}
              tabIndex={-1}
            >
              <header className="settings-section-header">
                <div>
                  <div className="settings-section-category">
                    {t(`settings.group.${activeSection.category}`)}
                  </div>
                  <h2 className="settings-section-title">{t(activeSection.titleKey)}</h2>
                </div>
                <span className={`settings-scope-badge ${activeSection.scope}`}>
                  {t(scopeLabelKey(activeSection.scope, overrideActive(activeSection)))}
                </span>
              </header>
              {activeSection.standalone ? (
                <div className="settings-standalone">{activeSection.body}</div>
              ) : (
                <div className="dashboard-card export-card">{activeSection.body}</div>
              )}
            </section>
          ) : null}
        </div>
      </div>
    </div>
  )
}
