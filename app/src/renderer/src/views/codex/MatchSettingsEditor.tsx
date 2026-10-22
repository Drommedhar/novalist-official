import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'
import type { SharedEntityFields } from '../../collaboration/FocusedField'
import { useSharedProjectRevision } from '../../collaboration/sharedProjectNotifications'
import { writeSharedMetadata } from '../../collaboration/metadataCommands'
import './match-settings.css'

interface MatchSettings {
  caseSensitive: boolean
  matchPlurals: boolean
  exclusions: string[]
  ignoredSceneIds: string[]
}

export function MatchSettingsEditor(props: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [settings, setSettings] = useState<MatchSettings | null>(null)
  const [draft, setDraft] = useState('')
  const record = useCodexStore(state => state.selectedRecord)
  const sharedFields = record?.id === props.entityId ? record.sharedFields as SharedEntityFields | undefined : undefined
  const revision = useSharedProjectRevision()

  const load = useCallback(async () => {
    setSettings(
      await rpc.request<MatchSettings>('entities/getMatchSettings', [
        props.entityType,
        props.entityId
      ])
    )
  }, [props.entityType, props.entityId])

  useEffect(() => {
    void load()
  }, [load, revision])

  const save = async (next: MatchSettings): Promise<void> => {
    setSettings(next)
    setSettings(
      await rpc.request<MatchSettings>('entities/setMatchSettings', [
        props.entityType,
        props.entityId,
        next.caseSensitive,
        next.matchPlurals,
        next.exclusions,
        next.ignoredSceneIds
      ])
    )
  }

  if (!settings) return <></>

  return (
    <div className="match-settings">
      {(['caseSensitive', 'matchPlurals'] as const).map(key => <div key={key}>
      <SharedMetadataField binding={metadataBinding({ sharedFields }, `match.${key}`)} value={settings[key]}
        onChange={value => void save({ ...settings, [key]: value })}>
      {field => <label className="match-toggle">
        <input
          type="checkbox"
          checked={field.value ?? false}
          aria-disabled={!field.editable}
          onChange={e => field.change(e.target.checked)}
        />
        {t(`match.${key}`)}
      </label>}
      </SharedMetadataField>
      <div className="match-hint">{t(`match.${key}Desc`)}</div>
      </div>)}

      <label className="inspector-label">{t('match.exclusions')}</label>
      <div className="match-hint">{t('match.exclusionsDesc')}</div>
      <SharedMetadataField binding={metadataBinding({ sharedFields }, 'match.exclusions')} value={settings.exclusions}
        onChange={exclusions => void save({ ...settings, exclusions })}>
      {field => {
        const addExclusion = (): void => {
          const phrase = draft.trim()
          if (!field.editable || !phrase) return
          field.change(previous => [...(previous ?? []), phrase])
          setDraft('')
        }
        return <>{(field.value ?? []).map((phrase) => (
        <div key={phrase} className="match-row">
          <span>{phrase}</span>
          <button
            className="dialog-button"
            title={t('match.removeExclusion')}
            aria-disabled={!field.editable}
            onClick={() => field.change(previous => previous.filter(e => e !== phrase))}
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}
      <div className="match-row">
        <input
          className="inspector-input"
          value={draft}
          readOnly={!field.editable}
          placeholder={t('match.exclusionPlaceholder')}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') addExclusion()
          }}
        />
        <button
          className="dialog-button"
          disabled={!field.editable || draft.trim().length === 0}
          title={t('match.addExclusion')}
          onClick={addExclusion}
        >
          <Plus size={14} />
        </button>
      </div>
      </>}}
      </SharedMetadataField>

      {settings.ignoredSceneIds.length > 0 && (
        <>
          <label className="inspector-label">{t('match.ignoredScenes')}</label>
          <div className="match-hint">
            {t('match.ignoredScenesCount', { count: settings.ignoredSceneIds.length })}
          </div>
          <div className="match-row">
            <button
              className="dialog-button"
              onClick={() => {
                const binding = metadataBinding({ sharedFields }, 'match.ignoredSceneIds')
                if (binding) void writeSharedMetadata(binding, [])
                else void save({ ...settings, ignoredSceneIds: [] })
              }}
            >
              {t('match.clearIgnoredScenes')}
            </button>
          </div>
        </>
      )}
    </div>
  )
}
