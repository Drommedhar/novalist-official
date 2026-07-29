import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import './match-settings.css'

interface MatchSettings {
  caseSensitive: boolean
  matchPlurals: boolean
  exclusions: string[]
  ignoredSceneIds: string[]
}

/**
 * Controls how this entry's name is recognised in prose.
 *
 * Every default reproduces the behaviour Novalist always had, so an existing
 * project reads identically until the writer changes something here.
 */
export function MatchSettingsEditor(props: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [settings, setSettings] = useState<MatchSettings | null>(null)
  const [draft, setDraft] = useState('')

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
  }, [load])

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

  const addExclusion = (): void => {
    const phrase = draft.trim()
    if (phrase.length === 0) return
    setDraft('')
    void save({ ...settings, exclusions: [...settings.exclusions, phrase] })
  }

  return (
    <div className="match-settings">
      <label className="match-toggle">
        <input
          type="checkbox"
          checked={settings.caseSensitive}
          onChange={(e) => void save({ ...settings, caseSensitive: e.target.checked })}
        />
        {t('match.caseSensitive')}
      </label>
      <div className="match-hint">{t('match.caseSensitiveDesc')}</div>

      <label className="match-toggle">
        <input
          type="checkbox"
          checked={settings.matchPlurals}
          onChange={(e) => void save({ ...settings, matchPlurals: e.target.checked })}
        />
        {t('match.matchPlurals')}
      </label>
      <div className="match-hint">{t('match.matchPluralsDesc')}</div>

      <label className="inspector-label">{t('match.exclusions')}</label>
      <div className="match-hint">{t('match.exclusionsDesc')}</div>
      {settings.exclusions.map((phrase) => (
        <div key={phrase} className="match-row">
          <span>{phrase}</span>
          <button
            className="dialog-button"
            title={t('match.removeExclusion')}
            onClick={() =>
              void save({
                ...settings,
                exclusions: settings.exclusions.filter((e) => e !== phrase)
              })
            }
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}
      <div className="match-row">
        <input
          className="inspector-input"
          value={draft}
          placeholder={t('match.exclusionPlaceholder')}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') addExclusion()
          }}
        />
        <button
          className="dialog-button"
          disabled={draft.trim().length === 0}
          title={t('match.addExclusion')}
          onClick={addExclusion}
        >
          <Plus size={14} />
        </button>
      </div>

      {settings.ignoredSceneIds.length > 0 && (
        <>
          <label className="inspector-label">{t('match.ignoredScenes')}</label>
          <div className="match-hint">
            {t('match.ignoredScenesCount', { count: settings.ignoredSceneIds.length })}
          </div>
          <div className="match-row">
            <button
              className="dialog-button"
              onClick={() => void save({ ...settings, ignoredSceneIds: [] })}
            >
              {t('match.clearIgnoredScenes')}
            </button>
          </div>
        </>
      )}
    </div>
  )
}
