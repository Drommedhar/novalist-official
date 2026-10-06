import {
  useEffect,
  useMemo,
  useRef,
  useState
} from 'react'
import { useTranslation } from 'react-i18next'
import { useIsPhone } from '../../shell/useIsPhone'
import { useOnboardingStore } from '../../stores/onboardingStore'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'
import { useThemeCatalog } from '../../stores/themeCatalog'
import { useUiScaleStore } from '../../stores/uiScaleStore'
import { assetDirectories } from '../../stores/userAssets'
import {
  parseSettingsDestination,
  setSettingsDestination,
  useSettingsNavigation
} from './settingsNavigation'
import {
  searchSettings,
  settingsControl,
  settingsSectionsForContext,
  type SettingsControlMetadata,
  type SettingsSectionKey,
  type SettingsSectionMetadata
} from './settingsRegistry'
import { DisplayDiagnostics } from './settingsViewTypes'
import { useSpeechVoices, useSystemVoices, useWritingLanguages } from './useSettingsOptions'
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
function useSettingsData() {
  const { t } = useTranslation()
  const view = useSettingsStore((s) => s.view)
  const load = useSettingsStore((s) => s.load)
  const update = useSettingsStore((s) => s.update)
  const pinSection = useSettingsStore((s) => s.pinSection)
  const clearSection = useSettingsStore((s) => s.clearSection)
  const updateProjectMeta = useSettingsStore((s) => s.updateProjectMeta)
  const themes = useThemeCatalog((s) => s.themes)
  const assetDirs = assetDirectories()
  const isMobile = window.novalist.isMobile === true
  const isPhone = useIsPhone()
  const projectLoaded = useProjectStore((s) => s.isLoaded)
  const projectName = useProjectStore((s) => s.projectName)
  const closeProject = useProjectStore((s) => s.closeProject)
  const setMainView = useShellStore((s) => s.setMainView)
  const uiScale = useUiScaleStore((s) => s.percent)
  const setUiScale = useUiScaleStore((s) => s.setPercent)
  const resetUiScale = useUiScaleStore((s) => s.reset)
  const tipsEnabled = useOnboardingStore((s) => s.tipsEnabled)
  const setTipsEnabled = useOnboardingStore((s) => s.setTipsEnabled)
  const [displayInfo, setDisplayInfo] = useState<DisplayDiagnostics | null>(null)
  const [displayInfoBusy, setDisplayInfoBusy] = useState(false)
  const voices = useSpeechVoices()
  const systemVoices = useSystemVoices(view !== null)
  const writingLanguages = useWritingLanguages()
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
  return {
    t, view, load, update,
    pinSection, clearSection, updateProjectMeta, themes,
    assetDirs, isMobile, isPhone, projectLoaded,
    projectName, setMainView, uiScale, setUiScale,
    resetUiScale, tipsEnabled, setTipsEnabled, voices,
    systemVoices, writingLanguages, displayInfo, displayInfoBusy,
    refreshDisplayInfo, closeProjectFromSettings
  }
}
function useSettingsRouting(data: ReturnType<typeof useSettingsData>) {
  const { t, view, load, projectLoaded, isMobile } = data
  const settingsSearch = useShellStore((s) => s.settingsSearch)
  const destination = useSettingsNavigation((s) => s.destination)
  const destinationRevision = useSettingsNavigation((s) => s.revision)
  const [search, setSearch] = useState(destination.query ?? '')
  const [selectedSection, setSelectedSection] = useState<SettingsSectionKey>(
    destination.section ?? 'appearance'
  )
  const sectionSurfaceRef = useRef<HTMLDivElement>(null)
  const sectionsRef = useRef<HTMLDivElement>(null)
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
  useDestinationEffects({
    view, projectLoaded, availableMetadata, settingsSearch,
    destination, destinationRevision, search, setSearch,
    selectedSection, setSelectedSection, sectionsRef
  })
  useControlFocus({ view, destination, destinationRevision, selectedSection, sectionSurfaceRef, t })
  return { destination, destinationRevision, search, setSearch, selectedSection, setSelectedSection, sectionSurfaceRef, sectionsRef, availableMetadata, searchResults }
}
interface DestinationEffects {
  view: ReturnType<typeof useSettingsData>['view']; projectLoaded: boolean; availableMetadata: readonly SettingsSectionMetadata[]
  settingsSearch: string; destination: ReturnType<typeof useSettingsNavigation.getState>['destination']; destinationRevision: number
  search: string; setSearch: React.Dispatch<React.SetStateAction<string>>; selectedSection: SettingsSectionKey
  setSelectedSection: React.Dispatch<React.SetStateAction<SettingsSectionKey>>; sectionsRef: React.RefObject<HTMLDivElement | null>
}
function useDestinationEffects({
    view, projectLoaded, availableMetadata, settingsSearch,
    destination, destinationRevision, search, setSearch,
    selectedSection, setSelectedSection, sectionsRef
  }: DestinationEffects) {
  useEffect(() => {
    if (settingsSearch) {
      const parsed = parseSettingsDestination(settingsSearch)
      if (parsed) {
        setSettingsDestination({ ...parsed, origin: destination.origin })
      } else {
        setSearch(settingsSearch)
      }
      if (useShellStore.getState().settingsSearch) useShellStore.setState({ settingsSearch: '' })
    }
  }, [destination.origin, settingsSearch])
  useEffect(() => {
    if (destination.query !== undefined) setSearch(destination.query)
    if (destination.section) setSelectedSection(destination.section)
  }, [destination, destinationRevision])
  useEffect(() => {
    // Opening a project refreshes settings asynchronously. The previous global
    // model (or no model yet) cannot decide whether its section is available.
    if (!view || view.hasProject !== projectLoaded) return
    if (availableMetadata.some((section) => section.key === selectedSection)) return
    const fallback = availableMetadata[0]?.key
    if (!fallback) return
    setSelectedSection(fallback)
    setSettingsDestination({ section: fallback, origin: destination.origin })
  }, [availableMetadata, destination.origin, projectLoaded, selectedSection, view])
  useEffect(() => {
    sectionsRef.current?.scrollTo({ top: 0 })
  }, [selectedSection, search])
}
function useControlFocus({ view, destination, destinationRevision, selectedSection, sectionSurfaceRef, t }: Pick<DestinationEffects, 'view' | 'destination' | 'destinationRevision' | 'selectedSection'> & { sectionSurfaceRef: React.RefObject<HTMLDivElement | null>; t: ReturnType<typeof useTranslation>['t'] }) {
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
}
export function useSettingsModel() {
  const data = useSettingsData()
  const routing = useSettingsRouting(data)
  return { ...data, ...routing }
}
