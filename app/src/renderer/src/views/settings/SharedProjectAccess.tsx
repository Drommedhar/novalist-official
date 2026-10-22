import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'

interface Member { userId: string; displayName: string; role: string }

export function SharedProjectAccess({ sessionId, disabled }: { sessionId: string; disabled: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const [members, setMembers] = useState<Member[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    const refresh = (): void => {
      if (disabled) return
      void rpc.request<Member[]>('collaboration/members').then(result => { if (active) setMembers(result) },
        reason => { if (active) setError(String(reason)) })
    }
    refresh()
    const timer = setInterval(refresh, 2000)
    return () => { active = false; clearInterval(timer) }
  }, [sessionId, disabled])

  const remove = async (member: Member): Promise<void> => {
    if (!window.confirm(t('collaboration.access.removeConfirm', { name: member.displayName }))) return
    setBusy(true); setError('')
    try {
      await rpc.request('collaboration/removeMember', [member.userId])
      useHostedSessionStore.setState({ invitation: '' })
      setMembers(await rpc.request<Member[]>('collaboration/members'))
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  const deleteProject = async (): Promise<void> => {
    if (!window.confirm(t('collaboration.access.deleteConfirm'))) return
    setBusy(true); setError('')
    try {
      await rpc.request('collaboration/deleteSharedProject')
      useHostedSessionStore.setState({ session: null, invitation: '', presenceFailures: [], presenceUnavailable: false })
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  return <div className="settings-subgroup">
    <h4>{t('collaboration.access.title')}</h4>
    <p className="settings-hint">{t('collaboration.access.hint')}</p>
    {members.map(member => <div className="match-row" key={member.userId}>
      <span>{member.displayName}</span>
      {member.role === 'Owner' ? <span>{t('collaboration.access.owner')}</span> :
        <button className="btn-secondary" disabled={disabled || busy} onClick={() => void remove(member)}>
          {t('collaboration.access.remove')}
        </button>}
    </div>)}
    <button className="btn-secondary" disabled={disabled || busy} onClick={() => void deleteProject()}>
      {t('collaboration.access.delete')}
    </button>
    {error && <p role="alert">{error}</p>}
  </div>
}
