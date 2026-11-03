import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'
import type { SharedEntityFields } from '../../collaboration/FocusedField'
import { useSharedProjectRevision } from '../../collaboration/sharedProjectNotifications'

interface AiSection {
  index: number
  title: string
  hidden: boolean
}

interface AiPolicy {
  /** "WhenMentioned" | "Always" | "Never" */
  inclusion: string
  sections: AiSection[]
}

const INCLUSIONS = ['WhenMentioned', 'Always', 'Never'] as const

/**
 * What an AI extension is allowed to see of this entry.
 *
 * The writer owns this rather than the extension. A twist they have not written
 * yet should not reach a model because something decided the entry looked
 * relevant, and there is no way to take it back afterwards.
 */
export function AiPolicyEditor(props: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [policy, setPolicy] = useState<AiPolicy | null>(null)
  const record = useCodexStore(state => state.selectedRecord)
  const sharedFields = record?.id === props.entityId ? record.sharedFields as SharedEntityFields | undefined : undefined
  const sections = record?.sections as { id: string }[] | undefined
  const revision = useSharedProjectRevision()

  const load = useCallback(async () => {
    setPolicy(
      await rpc.request<AiPolicy>('entities/getAiPolicy', [props.entityType, props.entityId])
    )
  }, [props.entityType, props.entityId])

  useEffect(() => {
    void load()
  }, [load, revision])

  if (!policy) return <></>

  const save = async (next: AiPolicy): Promise<void> => {
    setPolicy(next)
    setPolicy(
      await rpc.request<AiPolicy>('entities/setAiPolicy', [
        props.entityType,
        props.entityId,
        next.inclusion,
        next.sections.filter((s) => s.hidden).map((s) => s.index)
      ])
    )
  }

  return (
    <div className="match-settings">
      <label className="inspector-label">{t('aiPolicy.inclusion')}</label>
      <SharedMetadataField binding={metadataBinding({ sharedFields }, 'ai')} value={INCLUSIONS.findIndex(value => value === policy.inclusion)}
        onChange={index => void save({ ...policy, inclusion: INCLUSIONS[index] })}>
        {field => <>{INCLUSIONS.map((value, index) => (
        <label key={value} className="match-toggle">
          <input
            type="radio"
            name={`ai-inclusion-${props.entityId}`}
            checked={(field.value ?? 0) === index}
            aria-disabled={!field.editable}
            onChange={() => field.change(index)}
          />
          {t(`aiPolicy.${value}`)}
        </label>
      ))}
      <div className="match-hint">{t(`aiPolicy.${INCLUSIONS[field.value ?? 0]}Desc`)}</div></>}
      </SharedMetadataField>

      {policy.sections.length > 0 && (
        <>
          <label className="inspector-label">{t('aiPolicy.sections')}</label>
          <div className="match-hint">{t('aiPolicy.sectionsDesc')}</div>
          {policy.sections.map((section) => (
            <SharedMetadataField key={sections?.[section.index]?.id ?? section.index}
              binding={metadataBinding({ sharedFields }, `section:${sections?.[section.index]?.id}:aiHidden`)} value={section.hidden}
              onChange={hidden => void save({ ...policy, sections: policy.sections.map(s => s.index === section.index ? { ...s, hidden } : s) })}>
              {field => <label className="match-toggle">
              <input
                type="checkbox"
                checked={field.value ?? false}
                aria-disabled={!field.editable}
                disabled={policy.inclusion === 'Never'}
                onChange={e => field.change(e.target.checked)}
              />
              {section.title || t('aiPolicy.untitledSection')}
            </label>}
            </SharedMetadataField>
          ))}
        </>
      )}
    </div>
  )
}
