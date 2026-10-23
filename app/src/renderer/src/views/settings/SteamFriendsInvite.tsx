import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface SteamFriend { steamId: string; name: string; online: boolean }

export function SteamFriendsInvite({ disabled }: { disabled: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const [friends, setFriends] = useState<SteamFriend[] | null>(null)
  const [selected, setSelected] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [sent, setSent] = useState(false)
  const load = async (): Promise<void> => {
    setBusy(true); setError(''); setSent(false)
    try { setFriends(await rpc.request<SteamFriend[]>('collaboration/steam/friends')) }
    catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  const invite = async (): Promise<void> => {
    setBusy(true); setError(''); setSent(false)
    try { await rpc.request('collaboration/steam/inviteFriend', [selected]); setSent(true) }
    catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  return <div className="collaboration-project">
    <button className="dialog-button" disabled={disabled || busy} onClick={() => void load()}>
      {t('collaboration.steamInviteFriends')}
    </button>
    {friends && <>
      <label className="inspector-label" htmlFor="steam-invite-friend">{t('collaboration.steamChooseFriend')}</label>
      <select id="steam-invite-friend" className="dialog-input" value={selected} disabled={disabled || busy}
        onChange={event => { setSelected(event.target.value); setSent(false) }}>
        <option value="">{t('collaboration.steamChooseFriend')}</option>
        {friends.map(friend => <option key={friend.steamId} value={friend.steamId}>
          {friend.name}{friend.online ? '' : ' — ' + t('collaboration.steamFriendOffline')}
        </option>)}
      </select>
      {friends.length === 0 && <p role="status">{t('collaboration.steamNoFriends')}</p>}
      <button className="dialog-button primary" disabled={disabled || busy || !friends.some(friend => friend.steamId === selected)}
        onClick={() => void invite()}>{t('collaboration.steamSendInvitation')}</button>
    </>}
    {sent && <p role="status">{t('collaboration.steamInvitationSent')}</p>}
    {error && <p role="alert">{error}</p>}
  </div>
}
