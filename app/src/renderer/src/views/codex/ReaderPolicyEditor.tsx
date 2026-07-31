import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface ReaderSection {
  index: number
  title: string
  hidden: boolean
}

interface ReaderPolicy {
  hidden: boolean
  sections: ReaderSection[]
}

/**
 * What a reader is allowed to see of this entry.
 *
 * A separate axis from the AI policy on purpose: a writer may be happy for a
 * model to know the twist while they plan, and never for a reader to find it in
 * a world page handed round before the book is finished. One switch for both
 * would force a choice nobody should have to make.
 *
 * A hidden entry does not appear at all rather than appearing empty - listing
 * the name and withholding the fields announces that there is something to
 * find, which is most of the spoiler.
 */
export function ReaderPolicyEditor(props: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [policy, setPolicy] = useState<ReaderPolicy | null>(null)

  const load = useCallback(async () => {
    setPolicy(
      await rpc.request<ReaderPolicy>('entities/getReaderPolicy', [
        props.entityType,
        props.entityId
      ])
    )
  }, [props.entityType, props.entityId])

  useEffect(() => {
    void load()
  }, [load])

  const save = async (hidden: boolean, hiddenSections: number[]): Promise<void> => {
    setPolicy(
      await rpc.request<ReaderPolicy>('entities/setReaderPolicy', [
        props.entityType,
        props.entityId,
        hidden,
        hiddenSections
      ])
    )
  }

  if (!policy) return <p className="inspector-meta">{t('shell.backendConnecting')}</p>

  const hiddenSections = policy.sections.filter((s) => s.hidden).map((s) => s.index)

  return (
    <div className="ai-policy">
      <p className="inspector-meta">{t('readerPolicy.intro')}</p>

      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={policy.hidden}
          onChange={(e) => void save(e.target.checked, hiddenSections)}
        />
        {t('readerPolicy.hideEntry')}
      </label>

      {/* Per section, so one twist can be withheld while the character it
          belongs to still appears. */}
      {!policy.hidden && policy.sections.length > 0 && (
        <>
          <span className="inspector-label">{t('readerPolicy.sections')}</span>
          {policy.sections.map((section) => (
            <label key={section.index} className="relationships-toggle">
              <input
                type="checkbox"
                checked={section.hidden}
                onChange={(e) =>
                  void save(
                    policy.hidden,
                    e.target.checked
                      ? [...hiddenSections, section.index]
                      : hiddenSections.filter((i) => i !== section.index)
                  )
                }
              />
              {section.title}
            </label>
          ))}
        </>
      )}
    </div>
  )
}
