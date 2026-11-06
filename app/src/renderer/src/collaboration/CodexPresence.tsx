import { useEffect } from 'react'
import { Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { SharedEntityFields } from './FocusedField'
import type { SharedEntityProse } from './sharedMarkdown'
import { SharedItemPresence } from './SharedItemPresence'
import { useParticipants } from './participants'
import './scenePresence.css'

export function CodexPresence({ fields, prose, compact = false, publish = false }: {
  fields?: SharedEntityFields; prose?: SharedEntityProse; compact?: boolean; publish?: boolean
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const sessionId = fields?.sessionId
  const itemId = fields?.presenceItemId
  useEffect(() => {
    if (!publish || !sessionId || !itemId) return
    const presence = new SharedItemPresence({ sessionId, itemId })
    return () => presence.close()
  }, [publish, sessionId, itemId])
  const participants = useParticipants(sessionId)
  const editingItems = new Set([...Object.values(fields?.fields ?? {}), ...Object.values(fields?.metadata ?? {}),
    ...Object.values(prose?.fields ?? {})])
  const editors = new Map(participants.filter(participant => editingItems.has(participant.itemId))
    .map(participant => [participant.deviceId, participant.name]))
  const viewers = new Map(participants.filter(participant => participant.itemId === itemId && !editors.has(participant.deviceId))
    .map(participant => [participant.deviceId, participant.name]))
  if (!editors.size && !viewers.size) return null
  const label = [editors.size ? t('collaboration.sceneEditors', { names: [...editors.values()].join(', ') }) : '',
    viewers.size ? t('collaboration.entryViewers', { names: [...viewers.values()].join(', ') }) : ''].filter(Boolean).join(' · ')
  if (!compact) return <p className="collaboration-entry-presence" role="status">{label}</p>
  return <span className="collaboration-scene-badge" title={label} aria-label={label} role="img">
    <Users aria-hidden="true" /><span aria-hidden="true">{editors.size + viewers.size}</span>
  </span>
}
