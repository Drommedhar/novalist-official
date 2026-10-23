import { useEffect, useState } from 'react'
import { rpc } from '../rpc/client'
import { useHostedSessionStore } from './hostedSessionStore'

interface Account { subject: string; displayName: string }
const preference = 'novalist.collaboration.server'

export function useCollaborationAccount(enabled: boolean) {
  const [server, setServer] = useState(() => localStorage.getItem(preference) ?? '')
  const [account, setAccount] = useState<Account | null>(null)
  const [busy, setBusy] = useState<'checking' | 'signIn' | 'signOut' | null>(null)
  const [error, setError] = useState('')
  const savedServer = useHostedSessionStore(state => state.saved?.server)
  useEffect(() => {
    if (!enabled) return
    const saved = localStorage.getItem(preference) || savedServer
    if (!saved) return
    setServer(saved)
    let current = true
    setBusy('checking')
    void rpc.request<Account | null>('collaboration/account/status', [saved])
      .then(value => { if (current) setAccount(value) })
      .catch(reason => { if (current) setError(String(reason)) })
      .finally(() => { if (current) setBusy(null) })
    return () => { current = false }
  }, [savedServer, enabled])
  const signIn = async (): Promise<void> => {
    setError(''); setBusy('checking')
    const address = server.trim()
    try {
      let value = await rpc.request<Account | null>('collaboration/account/status', [address])
      if (!value) {
        setBusy('signIn')
        value = await rpc.request<Account>('collaboration/account/signIn', [address])
      }
      localStorage.setItem(preference, address)
      setAccount(value)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(null) }
  }
  const signOut = async (): Promise<void> => {
    setError(''); setBusy('signOut')
    try {
      await rpc.request('collaboration/account/signOut', [server.trim()])
      setAccount(null)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(null) }
  }
  const changeServer = (value: string): void => { setServer(value); setAccount(null); setError('') }
  const cancel = async (): Promise<void> => {
    try { await rpc.request('collaboration/account/cancel') }
    catch (reason) { setError(String(reason)) }
  }
  return { server, account, busy, error, signIn, signOut, changeServer, cancel }
}
