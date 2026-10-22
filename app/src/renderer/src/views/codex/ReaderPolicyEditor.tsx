import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'
import type { SharedEntityFields } from '../../collaboration/FocusedField'
import { useSharedProjectRevision } from '../../collaboration/sharedProjectNotifications'

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
  const record = useCodexStore(state => state.selectedRecord)
  const sharedFields = record?.id === props.entityId ? record.sharedFields as SharedEntityFields | undefined : undefined
  const sections = record?.sections as { id: string }[] | undefined
  const revision = useSharedProjectRevision()

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
  }, [load, revision])

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

      <SharedMetadataField binding={metadataBinding({ sharedFields }, 'readerHidden')} value={policy.hidden}
        onChange={hidden => void save(hidden, hiddenSections)}>
      {field => <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={field.value ?? false}
          aria-disabled={!field.editable}
          onChange={e => field.change(e.target.checked)}
        />
        {t('readerPolicy.hideEntry')}
      </label>}
      </SharedMetadataField>

      {/* Per section, so one twist can be withheld while the character it
          belongs to still appears. */}
      {!policy.hidden && policy.sections.length > 0 && (
        <>
          <span className="inspector-label">{t('readerPolicy.sections')}</span>
          {policy.sections.map((section) => (
            <SharedMetadataField key={sections?.[section.index]?.id ?? section.index}
              binding={metadataBinding({ sharedFields }, `section:${sections?.[section.index]?.id}:readerHidden`)} value={section.hidden}
              onChange={hidden => void save(policy.hidden, hidden ? [...hiddenSections, section.index] : hiddenSections.filter(i => i !== section.index))}>
            {field => <label className="relationships-toggle">
              <input
                type="checkbox"
                checked={field.value ?? false}
                aria-disabled={!field.editable}
                onChange={e => field.change(e.target.checked)}
              />
              {section.title}
            </label>}
            </SharedMetadataField>
          ))}
        </>
      )}
    </div>
  )
}
