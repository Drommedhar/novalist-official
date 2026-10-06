import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { type OverrideSection, type CharacterOverride, type Scope, apply } from './overrideModel'
import { OverrideMediaHeader } from './OverrideMediaHeader'

/** Per-scope override sections editor. Mirrors the sections block of
 * EntityListsEditor.tsx but persists to the override scope via
 * entities/setOverrideSections (null resets to inherit the base list). */
export function OverrideSections({
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
