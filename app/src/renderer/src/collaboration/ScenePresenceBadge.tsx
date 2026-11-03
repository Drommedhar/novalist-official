import { Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from './hostedSessionStore'
import { useParticipants } from './participants'
import './scenePresence.css'

export function ScenePresenceBadge({ sceneId }: { sceneId: string }): React.JSX.Element | null {
  const { t } = useTranslation()
  const sessionId = useHostedSessionStore(state => state.session?.sessionId)
  const participants = useParticipants(sessionId)
  const writers = new Map(participants.filter(participant => participant.sceneId === sceneId)
    .map(participant => [participant.deviceId, participant.name]))
  if (!writers.size) return null
  const label = t('collaboration.sceneEditors', { names: [...writers.values()].join(', ') })
  return <span className="collaboration-scene-badge" title={label} aria-label={label} role="img">
    <Users aria-hidden="true" /><span aria-hidden="true">{writers.size}</span>
  </span>
}
