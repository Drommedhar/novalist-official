import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useBookScope, useProjectStore } from '../../stores/projectStore'
import { useStageStore } from '../../stores/stageStore'
import { useShellStore } from '../../stores/shellStore'
import { flushPendingWrites } from '../../stores/pendingWrites'
import {
  codexLabels, ENTITY_KINDS, FORMATS, type Content, type EntityOption,
  type ExtensionFormatDto, type PresetDto, type PreviewDto
} from './exportTypes'

function useExportOptions() {
  const projectName = useProjectStore((s) => s.projectName)
  const [content, setContent] = useState<Content>('manuscript')
  // One remembered format per content, so switching across and back does not
  // silently reset the writer's choice.
  const [formats, setFormats] = useState<Record<Content, string>>({
    manuscript: 'Epub',
    codex: 'Codex',
    data: 'Csv',
    report: 'SynopsisReport'
  })
  const format = formats[content]
  const setFormat = (next: string): void => setFormats({ ...formats, [content]: next })
  const [presetId, setPresetId] = useState('default')
  const [presets, setPresets] = useState<PresetDto[]>([])
  const [extFormats, setExtFormats] = useState<ExtensionFormatDto[]>([])
  const [title, setTitle] = useState(projectName ?? '')
  const [author, setAuthor] = useState('')
  const [includeTitlePage, setIncludeTitlePage] = useState(true)
  // Off for Shunn: a submission manuscript does not carry a cover.
  const [includeCover, setIncludeCover] = useState(true)
  // A world page that lists the villain's real name beside everything else
  // is worse than no world page at all.
  const [forReaders, setForReaders] = useState(false)
  const [tocDepth, setTocDepth] = useState(1)
  const [tocTitle, setTocTitle] = useState('')
  const [referenceDoc, setReferenceDoc] = useState('')
  const [retailers, setRetailers] = useState<{ key: string; name: string }[]>([])
  const [retailerKey, setRetailerKey] = useState('')
  const [reviewOpen, setReviewOpen] = useState(false)
  useEffect(() => {
    // The same list the layout editor writes to, so a layout the writer
    // authored is pickable here the moment it exists.
    void rpc.request<PresetDto[]>('exportPresets/list').then(setPresets)
    void rpc.request<ExtensionFormatDto[]>('export/extensionFormats').then(setExtFormats)
  }, [])

  // The stores this book has links for, so a build can be made for one.
  useEffect(() => {
    void rpc
      .request<{ key: string; name: string }[]>('export/retailers')
      .then(setRetailers)
      .catch(() => setRetailers([]))
  }, [])

  const isData = content === 'data' || content === 'report'
  const chaptersVisible = content !== 'codex'
  const activePreset = presets.find((preset) => preset.id === presetId)
  return {
    projectName, content, setContent, format, setFormat, presetId, setPresetId,
    presets, setPresets, extFormats, title, setTitle, author, setAuthor,
    includeTitlePage, setIncludeTitlePage, includeCover, setIncludeCover,
    forReaders, setForReaders, tocDepth, setTocDepth, tocTitle, setTocTitle,
    referenceDoc, setReferenceDoc, retailers, retailerKey, setRetailerKey,
    reviewOpen, setReviewOpen, isData, chaptersVisible, activePreset
  }
}

function useExportSelection(options: ReturnType<typeof useExportOptions>) {
  const { setTitle, setContent, setIncludeTitlePage } = options
  const chapters = useProjectStore((s) => s.chapters)
  const pendingChapter = useShellStore((s) => s.pendingExportChapter)
  const bookScope = useBookScope()
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  const otherBooks = books.filter((b) => b.id !== activeBookId)
  const [stageFilter, setStageFilter] = useState<Set<string>>(new Set())
  const stages = useStageStore((st) => st.stages)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [initializedScope, setInitializedScope] = useState<string | null>(null)
  const [extraBooks, setExtraBooks] = useState<Set<string>>(new Set())
  // A binder action selects just that chapter, even when Export is already open.
  useEffect(() => {
    if (pendingChapter) {
      const chapter = chapters.find((c) => c.guid === pendingChapter)
      setSelected(new Set(chapter ? [chapter.guid] : []))
      if (chapter) setTitle(chapter.title)
      setContent('manuscript')
      setExtraBooks(new Set())
      setIncludeTitlePage(false)
      setInitializedScope(bookScope)
      useShellStore.setState({ pendingExportChapter: null })
      return
    }
    if (initializedScope !== bookScope && chapters.length > 0) {
      const remembered = window.novalist.isMobile
        ? useShellStore.getState().mobileExportSelection
        : null
      const selection =
        remembered?.scope === bookScope ? remembered.chapters : chapters.map((c) => c.guid)
      setSelected(new Set(selection.filter((id) => chapters.some((c) => c.guid === id))))
      setInitializedScope(bookScope)
    }
  }, [chapters, initializedScope, pendingChapter, bookScope])

  useEffect(() => {
    if (window.novalist.isMobile && initializedScope === bookScope && !pendingChapter) {
      useShellStore.setState({
        mobileExportSelection: { scope: bookScope, chapters: [...selected] }
      })
    }
  }, [selected, initializedScope, pendingChapter, bookScope])

  const toggle = (guid: string, checked: boolean): void => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (checked) next.add(guid)
      else next.delete(guid)
      return next
    })
  }

  return {
    chapters, otherBooks, stageFilter, setStageFilter, stages,
    selected, setSelected, extraBooks, setExtraBooks, toggle
  }
}

function useExportEntities(content: Content, format: string) {
  const isCodex = content === 'codex'
  const entitiesVisible = isCodex || format === 'Json'
  // Null means every part, which is what a codex export always carried.
  const [codexParts, setCodexParts] = useState<Set<string>>(
    new Set(['images', 'fields', 'relationships', 'sections'])
  )
  const [sectionTitles, setSectionTitles] = useState<string[]>([])
  const [pickedSections, setPickedSections] = useState<Set<string>>(new Set())
  const [entities, setEntities] = useState<Record<string, EntityOption[]>>({})
  const [entitiesLoaded, setEntitiesLoaded] = useState(false)
  const [selectedEntities, setSelectedEntities] = useState<Set<string>>(new Set())
  const [entityQuery, setEntityQuery] = useState('')
  // Load the codex entities the first time a codex format is picked; every
  // entry starts selected so the default export matches the old behaviour.
  useEffect(() => {
    if (!entitiesVisible || entitiesLoaded) return
    setEntitiesLoaded(true)
    void Promise.all(
      ENTITY_KINDS.map(async ({ kind }) => {
        const list = await rpc
          .request<{ id: string; name: string }[]>('entities/list', [kind])
          .catch(() => [])
        const sorted = list
          .map((e) => ({ key: `${kind}:${e.id}`, name: e.name }))
          .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
        return [kind, sorted] as const
      })
    ).then((loaded) => {
      setEntities(Object.fromEntries(loaded))
      setSelectedEntities(new Set(loaded.flatMap(([, list]) => list.map((e) => e.key))))
    })
  }, [entitiesVisible, entitiesLoaded])

  // The titles this project actually uses, so the picker names them rather than
  // asking for them to be typed the same way twice.
  useEffect(() => {
    if (!isCodex) return
    void rpc
      .request<string[]>('export/codexSections')
      .then((titles) => {
        setSectionTitles(titles)
        setPickedSections(new Set(titles))
      })
      .catch(() => setSectionTitles([]))
  }, [isCodex])

  const allEntities = ENTITY_KINDS.flatMap(({ kind }) => entities[kind] ?? [])
  // Same grouping and name order the export itself writes, filtered by the search box.
  const needle = entityQuery.trim().toLocaleLowerCase()
  const visibleEntities = ENTITY_KINDS.map(({ kind, labelKey }) => ({
    kind,
    labelKey,
    list: (entities[kind] ?? []).filter((e) => e.name.toLocaleLowerCase().includes(needle))
  })).filter((group) => group.list.length > 0)
  const visibleKeys = visibleEntities.flatMap((group) => group.list.map((e) => e.key))
  const toggleEntity = (key: string, checked: boolean): void => {
    setSelectedEntities((prev) => {
      const next = new Set(prev)
      if (checked) next.add(key)
      else next.delete(key)
      return next
    })
  }

  return {
    isCodex, entitiesVisible, codexParts, setCodexParts, sectionTitles,
    pickedSections, setPickedSections, selectedEntities, setSelectedEntities,
    entityQuery, setEntityQuery, allEntities, visibleEntities, visibleKeys, toggleEntity
  }
}

type ExportInputs = ReturnType<typeof useExportOptions>
  & ReturnType<typeof useExportSelection>
  & ReturnType<typeof useExportEntities>

function useExportPreview(model: ExportInputs) {
  const { chaptersVisible, isData, selected, presetId, stageFilter } = model
  const [preview, setPreview] = useState<PreviewDto | null>(null)
  // What the current selection would actually produce. Recomputed whenever a
  // choice that changes it changes, so the writer never exports blind.
  useEffect(() => {
    // A metadata sheet has no pages and no compiled word count, and reporting
    // the manuscript's would be answering a question nobody asked.
    if (!chaptersVisible || isData) {
      setPreview(null)
      return
    }
    let current = true
    void rpc
      .request<PreviewDto>('export/preview', [[...selected], presetId, [...stageFilter]])
      .then((result) => {
        if (current) setPreview(result)
      })
      .catch(() => {
        if (current) setPreview(null)
      })
    return () => {
      current = false
    }
  }, [chaptersVisible, isData, selected, presetId, stageFilter])

  return preview
}

function useExportRun(model: ExportInputs) {
  const { t } = useTranslation()
  const {
    extFormats, format, title, author, includeTitlePage, chaptersVisible, selected,
    presetId, entitiesVisible, selectedEntities, isCodex, includeCover, stageFilter,
    tocDepth, tocTitle, referenceDoc, codexParts, pickedSections, sectionTitles,
    retailerKey, extraBooks, forReaders
  } = model
  const extFormat = extFormats.find((entry) => entry.formatKey === format)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const run = async (): Promise<void> => {
    if (busy) return
    let output: string | null = null
    setBusy(true)
    setResult(null)
    try {
      const extension =
        extFormat?.fileExtension ?? FORMATS.find((f) => f.format === format)?.extension ?? ''
      output = await window.novalist.saveFile(`${title || 'manuscript'}${extension}`)
      if (!output) return
      await flushPendingWrites()
      await useProjectStore.getState().flushPendingSave()
      const exported = await rpc.request<{ outputPath: string; success: boolean }>('export/run', [
        format,
        output,
        title,
        author,
        includeTitlePage,
        chaptersVisible ? [...selected] : [],
        presetId,
        entitiesVisible ? [...selectedEntities] : null,
        isCodex ? codexLabels(t) : null,
        includeCover,
        [...stageFilter],
        tocDepth,
        tocTitle,
        referenceDoc,
        isCodex ? [...codexParts] : null,
        // Naming every title is the same as naming none, and sending null keeps
        // the payload the size it was.
        isCodex && pickedSections.size < sectionTitles.length ? [...pickedSections] : null,
        retailerKey || null,
        chaptersVisible && extraBooks.size > 0 ? [...extraBooks] : null,
        forReaders
      ])
      if (!exported.success) {
        setResult(t('export.exportFailed'))
      } else if (window.novalist.isMobile) {
        if (!window.novalist.shareExport) throw new Error('Export sharing is unavailable')
        const shared = await window.novalist.shareExport(output)
        setResult(t(shared ? 'export.exportSuccess' : 'export.exportCancelled'))
      } else {
        setResult(t('export.exportSuccess'))
      }
    } catch {
      setResult(t('export.exportFailed'))
    } finally {
      if (output && window.novalist.releaseExport) {
        // A cleanup error must not change the result of a completed share.
        await window.novalist.releaseExport(output).catch(() => {})
      }
      setBusy(false)
    }
  }

  return { busy, result, run }
}

export function useExportModel() {
  const options = useExportOptions()
  const selection = useExportSelection(options)
  const entities = useExportEntities(options.content, options.format)
  const model = { ...options, ...selection, ...entities }
  const preview = useExportPreview(model)
  const execution = useExportRun(model)
  const exportDisabled = execution.busy
    || (model.chaptersVisible && model.selected.size === 0)
    || (model.entitiesVisible && model.allEntities.length > 0 && model.selectedEntities.size === 0)
  return { ...model, ...execution, preview, exportDisabled, isAudiobook: model.format === 'Audiobook' }
}

export type ExportModel = ReturnType<typeof useExportModel>
