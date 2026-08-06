import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ClipboardPaste, ImagePlus, Link, Plus, Pencil, RotateCcw, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { useProjectStore } from '../../stores/projectStore'
import './entity-images.css'

interface OverrideImage {
  name: string
  path: string
  /** Project-root-relative display URL resolved by the backend. */
  url?: string
}

interface OverrideRelationship {
  role: string
  target: string
  inverseRole?: string
}

interface OverrideSection {
  title: string
  content: string
}

interface CharacterOverride {
  act: string | null
  chapter: string
  scene: string | null
  customProperties?: Record<string, string> | null
  /** null = inherit the base list; an array (possibly empty) replaces it. */
  images?: OverrideImage[] | null
  relationships?: OverrideRelationship[] | null
  sections?: OverrideSection[] | null
  [field: string]: unknown
}

interface OverrideField {
  key: string
  labelKey: string
  multiline?: boolean
}

interface OverrideGroup {
  titleKey: string
  fields: OverrideField[]
}

/** The full character field set that a chapter/scene override can restate,
 * grouped like the base editor. Diff-stored: a blank input inherits the base. */
const OVERRIDE_GROUPS: OverrideGroup[] = [
  {
    titleKey: 'entityEditor.basicInfo',
    fields: [
      { key: 'name', labelKey: 'entityEditor.name' },
      { key: 'surname', labelKey: 'entityEditor.surname' },
      { key: 'role', labelKey: 'entityEditor.rolePlaceholder' },
      { key: 'gender', labelKey: 'entityEditor.gender' },
      { key: 'age', labelKey: 'entityEditor.age' }
    ]
  },
  {
    titleKey: 'entityEditor.physicalAttributes',
    fields: [
      { key: 'eyeColor', labelKey: 'entityEditor.eyeColor' },
      { key: 'hairColor', labelKey: 'entityEditor.hairColor' },
      { key: 'hairLength', labelKey: 'entityEditor.hairLength' },
      { key: 'height', labelKey: 'entityEditor.height' },
      { key: 'build', labelKey: 'entityEditor.build' },
      { key: 'skinTone', labelKey: 'entityEditor.skinTone' },
      { key: 'distinguishingFeatures', labelKey: 'entityEditor.distinguishingFeatures', multiline: true }
    ]
  }
]

const OVERRIDABLE = OVERRIDE_GROUPS.flatMap((g) => g.fields)

type Scope = { chapter: string; scene: string | null }

const scopeKey = (chapter: string, scene: string | null): string => `${chapter}|${scene ?? ''}`

const matchesScope = (over: CharacterOverride, scope: Scope): boolean =>
  over.chapter === scope.chapter && (over.scene ?? null) === scope.scene

const apply = (updated: Record<string, unknown>): void => {
  useCodexStore.setState({ selectedRecord: updated })
}

/** Header row for one of the per-scope media override editors: a label plus a
 * "reset to inherit" affordance shown only while the scope owns the list. */
function OverrideMediaHeader({
  labelKey,
  overriding,
  onReset
}: {
  labelKey: string
  overriding: boolean
  onReset: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="overrides-media-head">
      <span className="inspector-label">{t(labelKey)}</span>
      {overriding ? (
        <button className="overrides-reset" onClick={onReset}>
          <RotateCcw size={11} strokeWidth={2} />
          {t('entityEditor.overrideResetToInherit')}
        </button>
      ) : (
        <span className="overrides-inherit-tag">{t('entityEditor.overrideInheriting')}</span>
      )}
    </div>
  )
}

/** Per-scope override image strip: gallery pick, file import, clipboard paste and
 * download-from-URL, plus per-image remove/rename and reset-to-inherit. Mirrors
 * EntityImages.tsx but persists to the override scope via entities/setOverride*. */
function OverrideImages({
  selectedId,
  scope,
  override,
  baseImages
}: {
  selectedId: string
  scope: Scope
  override: CharacterOverride | undefined
  baseImages: OverrideImage[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const overriding = Array.isArray(override?.images)
  const images = overriding ? (override!.images as OverrideImage[]) : baseImages
  const [galleryOpen, setGalleryOpen] = useState(false)
  const [galleryImages, setGalleryImages] = useState<{ path: string; url: string }[]>([])
  const [urlOpen, setUrlOpen] = useState(false)
  const [urlValue, setUrlValue] = useState('')
  const [urlError, setUrlError] = useState(false)
  const [urlBusy, setUrlBusy] = useState(false)

  const args = [selectedId, scope.chapter, scope.scene] as const

  const setImages = (next: OverrideImage[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideImages', [
        ...args,
        next?.map((i) => ({ name: i.name, path: i.path })) ?? null
      ])
      .then(apply)
  }

  const openGallery = async (): Promise<void> => {
    setGalleryImages(await rpc.request<{ path: string; url: string }[]>('gallery/list'))
    setGalleryOpen(true)
  }

  const pickFromGallery = (imagePath: string): void => {
    setGalleryOpen(false)
    const name = imagePath.split('/').pop()?.replace(/\.[^.]+$/, '') ?? imagePath
    setImages([...images, { name, path: imagePath }])
  }

  const importImage = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('entityEditor.addImage'), 'images')
    if (!path) return
    apply(await rpc.request<Record<string, unknown>>('entities/addOverrideImage', [...args, path]))
  }

  const pasteImage = async (): Promise<void> => {
    const path = await window.novalist.readClipboardImage()
    if (!path) return
    apply(await rpc.request<Record<string, unknown>>('entities/addOverrideImage', [...args, path]))
  }

  const submitUrl = async (): Promise<void> => {
    const url = urlValue.trim()
    if (!url) return
    setUrlBusy(true)
    setUrlError(false)
    try {
      apply(
        await rpc.request<Record<string, unknown>>('entities/addOverrideImageFromUrl', [...args, url])
      )
      setUrlOpen(false)
      setUrlValue('')
    } catch {
      setUrlError(true)
    } finally {
      setUrlBusy(false)
    }
  }

  const renameImage = (path: string, currentName: string, nextName: string): void => {
    if (nextName === currentName) return
    setImages(images.map((i) => (i.path === path ? { ...i, name: nextName } : i)))
  }

  return (
    <div className="entity-images overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.images"
        overriding={overriding}
        onReset={() => setImages(null)}
      />
      <div className="entity-images-strip">
        {images.map((image) => (
          <figure key={image.path} className="entity-image">
            <img
              src={`novalist-project://nl/${encodeURI(image.url ?? image.path)}`}
              alt={image.name}
            />
            <div className="entity-image-actions">
              <button
                className="entity-image-remove"
                aria-label={`${t('entityEditor.removeImageTooltip')} ${image.name}`}
                title={t('entityEditor.removeImageTooltip')}
                onClick={() => setImages(images.filter((i) => i.path !== image.path))}
              >
                <X size={11} strokeWidth={2} />
              </button>
            </div>
            <input
              className="entity-image-namefield"
              aria-label={t('entityEditor.imageName')}
              placeholder={t('entityEditor.imageName')}
              defaultValue={image.name}
              key={`${image.path}:${image.name}`}
              onBlur={(e) => renameImage(image.path, image.name, e.target.value)}
            />
          </figure>
        ))}
        <button className="entity-image-add" onClick={() => void openGallery()}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.fromGallery')}
        </button>
        <button className="entity-image-add" onClick={() => void importImage()}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.importImage')}
        </button>
        <button className="entity-image-add" onClick={() => void pasteImage()}>
          <ClipboardPaste size={16} strokeWidth={1.75} />
          {t('entityEditor.pasteImage')}
        </button>
        <button
          className="entity-image-add"
          onClick={() => {
            setUrlValue('')
            setUrlError(false)
            setUrlOpen(true)
          }}
        >
          <Link size={16} strokeWidth={1.75} />
          {t('entityEditor.fromUrl')}
        </button>
      </div>
      {galleryOpen && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setGalleryOpen(false)}
        >
          <div className="dialog-card entity-gallery-card" role="dialog">
            <div className="dialog-title">{t('entityEditor.fromGallery')}</div>
            <div className="gallery-grid entity-gallery-grid">
              {galleryImages.map((img) => (
                <button
                  key={img.path}
                  className="entity-gallery-pick"
                  onClick={() => pickFromGallery(img.path)}
                >
                  <img src={`novalist-project://nl/${encodeURI(img.url)}`} alt={img.path} loading="lazy" />
                </button>
              ))}
              {galleryImages.length === 0 && (
                <p className="codex-empty">{t('imageGallery.noImages')}</p>
              )}
            </div>
          </div>
        </div>
      )}
      {urlOpen && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setUrlOpen(false)}
        >
          <div className="dialog-card" role="dialog">
            <div className="dialog-title">{t('entityEditor.fromUrlTitle')}</div>
            <input
              className="dialog-input entity-url-input"
              type="url"
              autoFocus
              placeholder={t('entityEditor.fromUrlPlaceholder')}
              value={urlValue}
              onChange={(e) => setUrlValue(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void submitUrl()}
            />
            {urlError && <div className="entity-url-error">{t('entityEditor.fromUrlError')}</div>}
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setUrlOpen(false)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button"
                disabled={urlBusy || urlValue.trim().length === 0}
                onClick={() => void submitUrl()}
              >
                {t('dialog.ok')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/** Per-scope override relationships editor. Mirrors the relationships block of
 * EntityListsEditor.tsx but persists to the override scope via
 * entities/setOverrideRelationships (null resets to inherit the base list). */
function OverrideRelationships({
  selectedId,
  scope,
  override,
  baseRelationships
}: {
  selectedId: string
  scope: Scope
  override: CharacterOverride | undefined
  baseRelationships: OverrideRelationship[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const overriding = Array.isArray(override?.relationships)
  const [rows, setRows] = useState<OverrideRelationship[]>([])
  const [nameSuggestions, setNameSuggestions] = useState<string[]>([])
  const [roleSuggestions, setRoleSuggestions] = useState<string[]>([])

  const effective = overriding ? (override!.relationships as OverrideRelationship[]) : baseRelationships

  useEffect(() => {
    setRows(effective.map((r) => ({ ...r })))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, scope.chapter, scope.scene, overriding, JSON.stringify(effective)])

  useEffect(() => {
    void rpc
      .request<{ characterNames: string[]; roles: string[] }>('entities/relationshipSuggestions')
      .then((s) => {
        setNameSuggestions(s.characterNames)
        setRoleSuggestions(s.roles)
      })
      .catch(() => {})
  }, [selectedId])

  const persist = (next: OverrideRelationship[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideRelationships', [
        selectedId,
        scope.chapter,
        scope.scene,
        next?.map((r) => ({ role: r.role, target: r.target })) ?? null
      ])
      .then(apply)
  }

  return (
    <div className="overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.relationships"
        overriding={overriding}
        onReset={() => persist(null)}
      />
      <datalist id="codex-override-rel-names">
        {nameSuggestions.map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>
      <datalist id="codex-override-rel-roles">
        {roleSuggestions.map((r) => (
          <option key={r} value={r} />
        ))}
      </datalist>
      {rows.map((rel, index) => {
        const patch = (p: Partial<OverrideRelationship>): void =>
          setRows(rows.map((r, i) => (i === index ? { ...r, ...p } : r)))
        return (
          <div key={index} className="entity-rel-row">
            <input
              className="outliner-input"
              list="codex-override-rel-roles"
              placeholder={t('entityEditor.rolePlaceholderRel')}
              value={rel.role}
              onChange={(e) => patch({ role: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <input
              className="outliner-input"
              list="codex-override-rel-names"
              placeholder={t('entityEditor.targetNames')}
              value={rel.target}
              onChange={(e) => patch({ target: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <button
              className="binder-expand"
              aria-label={t('explorer.contextDelete')}
              onClick={() => {
                const next = rows.filter((_, i) => i !== index)
                setRows(next)
                persist(next)
              }}
            >
              <X size={12} strokeWidth={2} />
            </button>
          </div>
        )
      })}
      <button
        className="binder-rail-item"
        onClick={() => setRows([...rows, { role: '', target: '' }])}
      >
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addRelationship')}
      </button>
    </div>
  )
}

/** Per-scope override sections editor. Mirrors the sections block of
 * EntityListsEditor.tsx but persists to the override scope via
 * entities/setOverrideSections (null resets to inherit the base list). */
function OverrideSections({
  selectedId,
  scope,
  override,
  baseSections
}: {
  selectedId: string
  scope: Scope
  override: CharacterOverride | undefined
  baseSections: OverrideSection[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const overriding = Array.isArray(override?.sections)
  const [sections, setSections] = useState<OverrideSection[]>([])

  const effective = overriding ? (override!.sections as OverrideSection[]) : baseSections

  useEffect(() => {
    setSections(effective.map((s) => ({ ...s })))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, scope.chapter, scope.scene, overriding, JSON.stringify(effective)])

  const persist = (next: OverrideSection[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideSections', [
        selectedId,
        scope.chapter,
        scope.scene,
        next?.map((s) => ({ title: s.title, content: s.content })) ?? null
      ])
      .then(apply)
  }

  return (
    <div className="overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.sections"
        overriding={overriding}
        onReset={() => persist(null)}
      />
      {sections.map((section, index) => (
        <div key={index} className="entity-section">
          <div className="entity-section-head">
            <input
              className="outliner-input entity-section-title"
              value={section.title}
              onChange={(e) =>
                setSections(sections.map((s, i) => (i === index ? { ...s, title: e.target.value } : s)))
              }
              onBlur={() => persist(sections)}
            />
            <button
              className="binder-expand"
              aria-label={t('explorer.contextDelete')}
              onClick={() => {
                const next = sections.filter((_, i) => i !== index)
                setSections(next)
                persist(next)
              }}
            >
              <X size={12} strokeWidth={2} />
            </button>
          </div>
          <MarkdownEditor
            value={section.content}
            ariaLabel={section.title}
            onChange={(next) =>
              setSections(sections.map((s, i) => (i === index ? { ...s, content: next } : s)))
            }
            onBlur={() => persist(sections)}
          />
        </div>
      ))}
      <button
        className="binder-rail-item"
        onClick={() => setSections([...sections, { title: t('section.newSection'), content: '' }])}
      >
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addSection')}
      </button>
    </div>
  )
}

/** Per-chapter/scene character overrides. Editing happens INLINE in the detail
 * pane (an expandable form under the scope row / add button), never in a modal —
 * mirroring the Avalonia entity editor. Diff-stored, blank = inherit. Covers the
 * full identity + physical field set, per-scope custom-property overrides, and —
 * for an already-saved scope — per-scope image, relationship, and section
 * overrides; resolved values surface in the focus-peek card and context sidebar. */
export function OverridesEditor(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const chapters = useProjectStore((s) => s.chapters)
  const [editing, setEditing] = useState<Scope | 'new' | null>(null)
  const [newScope, setNewScope] = useState<Scope>({ chapter: '', scene: null })
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [customDraft, setCustomDraft] = useState<Record<string, string>>({})

  if (entityType !== 'character' || !record || !selectedId) return null
  const overrides = Array.isArray(record.chapterOverrides)
    ? (record.chapterOverrides as CharacterOverride[])
    : []

  const baseCustom =
    record.customProperties && typeof record.customProperties === 'object'
      ? (record.customProperties as Record<string, string>)
      : {}
  const customKeys = Object.keys(baseCustom)

  const baseImages = Array.isArray(record.images) ? (record.images as OverrideImage[]) : []
  const baseRelationships = Array.isArray(record.relationships)
    ? (record.relationships as OverrideRelationship[])
    : []
  const baseSections = Array.isArray(record.sections) ? (record.sections as OverrideSection[]) : []

  const chapterTitle = (guid: string): string =>
    chapters.find((c) => c.guid === guid)?.title ?? guid

  const scopeLabel = (over: CharacterOverride): string =>
    over.scene ? `${chapterTitle(over.chapter)} → ${over.scene}` : chapterTitle(over.chapter)

  const overriddenLabels = (over: CharacterOverride): string => {
    const parts = OVERRIDABLE.filter(
      (f) => typeof over[f.key] === 'string' && (over[f.key] as string).length > 0
    ).map((f) => t(f.labelKey))
    if (over.customProperties && Object.keys(over.customProperties).length > 0)
      parts.push(t('entityEditor.customProperties'))
    if (Array.isArray(over.images)) parts.push(t('entityEditor.images'))
    if (Array.isArray(over.relationships)) parts.push(t('entityEditor.relationships'))
    if (Array.isArray(over.sections)) parts.push(t('entityEditor.sections'))
    return parts.join(', ')
  }

  const openEditor = (target: Scope | 'new'): void => {
    const scope = target === 'new' ? { chapter: chapters[0]?.guid ?? '', scene: null } : target
    const existing = overrides.find((o) => matchesScope(o, scope))
    const values: Record<string, string> = {}
    for (const field of OVERRIDABLE) {
      const value = existing?.[field.key]
      values[field.key] = typeof value === 'string' ? value : ''
    }
    const custom: Record<string, string> = {}
    for (const key of customKeys) custom[key] = existing?.customProperties?.[key] ?? ''
    setDraft(values)
    setCustomDraft(custom)
    if (target === 'new') setNewScope(scope)
    setEditing(target === 'new' ? 'new' : scope)
  }

  const removeOverride = (over: CharacterOverride): void => {
    setEditing(null)
    void rpc
      .request<Record<string, unknown>>('entities/removeOverride', [
        selectedId,
        over.chapter,
        over.scene ?? null
      ])
      .then(apply)
  }

  const save = (scope: Scope): void => {
    setEditing(null)
    void rpc
      .request<Record<string, unknown>>('entities/setOverride', [
        selectedId,
        scope.chapter,
        scope.scene,
        draft,
        customDraft
      ])
      .then(apply)
  }

  const isEditing = (over: CharacterOverride): boolean =>
    editing !== null && editing !== 'new' && matchesScope(over, editing)

  const newScopeScenes = chapters.find((c) => c.guid === newScope.chapter)?.scenes ?? []

  const form = (scope: Scope, pickScope: boolean): React.JSX.Element => {
    const existing = overrides.find((o) => matchesScope(o, scope))
    return (
      <div className="overrides-inline" role="group" aria-label={t('entityEditor.chapterOverrides')}>
        {pickScope && (
          <div className="overrides-scope-pickers">
            <select
              className="dialog-input"
              aria-label={t('entityEditor.chapterOverrides')}
              value={scope.chapter}
              onChange={(e) => setNewScope({ chapter: e.target.value, scene: null })}
            >
              {chapters.map((c) => (
                <option key={c.guid} value={c.guid}>
                  {c.title}
                </option>
              ))}
            </select>
            <select
              className="dialog-input"
              aria-label={t('entityEditor.wholeChapter')}
              value={scope.scene ?? ''}
              onChange={(e) => setNewScope({ chapter: scope.chapter, scene: e.target.value || null })}
            >
              <option value="">{t('entityEditor.wholeChapter')}</option>
              {newScopeScenes.map((s) => (
                <option key={s.id} value={s.title}>
                  {s.title}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="overrides-form">
          {OVERRIDE_GROUPS.map((group) => (
            <div key={group.titleKey} className="codex-field-section">
              <div className="inspector-label">{t(group.titleKey)}</div>
              {group.fields.map((field) => (
                <div key={field.key} className="codex-field">
                  <dt>{t(field.labelKey)}</dt>
                  <dd>
                    {field.multiline ? (
                      <MarkdownEditor
                        className="md-compact"
                        minRows={2}
                        placeholder={String(record[field.key] ?? '')}
                        ariaLabel={t(field.labelKey)}
                        value={draft[field.key] ?? ''}
                        onChange={(next) => setDraft({ ...draft, [field.key]: next })}
                      />
                    ) : (
                      <input
                        className="outliner-input codex-field-input"
                        placeholder={String(record[field.key] ?? '')}
                        value={draft[field.key] ?? ''}
                        onChange={(e) => setDraft({ ...draft, [field.key]: e.target.value })}
                      />
                    )}
                  </dd>
                </div>
              ))}
            </div>
          ))}
          {customKeys.length > 0 && (
            <div className="codex-field-section">
              <div className="inspector-label">{t('entityEditor.customProperties')}</div>
              {customKeys.map((key) => (
                <div key={key} className="codex-field">
                  <dt>{key}</dt>
                  <dd>
                    <input
                      className="outliner-input codex-field-input"
                      placeholder={baseCustom[key]}
                      value={customDraft[key] ?? ''}
                      onChange={(e) => setCustomDraft({ ...customDraft, [key]: e.target.value })}
                    />
                  </dd>
                </div>
              ))}
            </div>
          )}
        </div>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={() => setEditing(null)}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={() => save(scope)}>
            {t('dialog.save')}
          </button>
        </div>
        {pickScope ? (
          <p className="codex-empty overrides-empty">{t('entityEditor.overrideMediaHint')}</p>
        ) : (
          <div className="overrides-media-group">
            <OverrideImages
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseImages={baseImages}
            />
            <OverrideRelationships
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseRelationships={baseRelationships}
            />
            <OverrideSections
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseSections={baseSections}
            />
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.chapterOverrides')}</div>
      {overrides.length === 0 && editing !== 'new' && (
        <p className="codex-empty overrides-empty">{t('entityEditor.overridesHint')}</p>
      )}
      <div className="overrides-list">
        {overrides.map((over) => (
          <div key={scopeKey(over.chapter, over.scene ?? null)} className="overrides-item">
            <div className="overrides-row">
              <button
                className="overrides-scope"
                onClick={() =>
                  isEditing(over)
                    ? setEditing(null)
                    : openEditor({ chapter: over.chapter, scene: over.scene ?? null })
                }
              >
                <span className="overrides-scope-label">{scopeLabel(over)}</span>
                <span className="overrides-scope-fields">
                  {overriddenLabels(over) || t('entityEditor.overridesNoneSet')}
                </span>
              </button>
              <button
                className="binder-expand"
                aria-label={t('entityEditor.editOverride')}
                onClick={() =>
                  isEditing(over)
                    ? setEditing(null)
                    : openEditor({ chapter: over.chapter, scene: over.scene ?? null })
                }
              >
                <Pencil size={12} strokeWidth={2} />
              </button>
              <button
                className="binder-expand"
                aria-label={t('explorer.contextDelete')}
                onClick={() => removeOverride(over)}
              >
                <X size={12} strokeWidth={2} />
              </button>
            </div>
            {isEditing(over) && form({ chapter: over.chapter, scene: over.scene ?? null }, false)}
          </div>
        ))}
      </div>
      {editing === 'new' ? (
        form(newScope, true)
      ) : (
        chapters.length > 0 && (
          <button className="binder-rail-item" onClick={() => openEditor('new')}>
            <Plus size={13} strokeWidth={2} />
            {t('entityEditor.addOverride')}
          </button>
        )
      )}
    </div>
  )
}
