import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

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

  const load = useCallback(async () => {
    setPolicy(
      await rpc.request<AiPolicy>('entities/getAiPolicy', [props.entityType, props.entityId])
    )
  }, [props.entityType, props.entityId])

  useEffect(() => {
    void load()
  }, [load])

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
      {INCLUSIONS.map((value) => (
        <label key={value} className="match-toggle">
          <input
            type="radio"
            name={`ai-inclusion-${props.entityId}`}
            checked={policy.inclusion === value}
            onChange={() => void save({ ...policy, inclusion: value })}
          />
          {t(`aiPolicy.${value}`)}
        </label>
      ))}
      <div className="match-hint">{t(`aiPolicy.${policy.inclusion}Desc`)}</div>

      {policy.sections.length > 0 && (
        <>
          <label className="inspector-label">{t('aiPolicy.sections')}</label>
          <div className="match-hint">{t('aiPolicy.sectionsDesc')}</div>
          {policy.sections.map((section) => (
            <label key={section.index} className="match-toggle">
              <input
                type="checkbox"
                checked={section.hidden}
                disabled={policy.inclusion === 'Never'}
                onChange={(e) =>
                  void save({
                    ...policy,
                    sections: policy.sections.map((s) =>
                      s.index === section.index ? { ...s, hidden: e.target.checked } : s
                    )
                  })
                }
              />
              {section.title || t('aiPolicy.untitledSection')}
            </label>
          ))}
        </>
      )}
    </div>
  )
}
