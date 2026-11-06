import { useEffect, useRef, useState } from 'react'
import { rpc } from '../rpc/client'
import { useHostedSessionStore } from './hostedSessionStore'

interface Account { subject: string; displayName: string }
interface Configuration { authenticationRequired: boolean }
const preference = 'novalist.collaboration.server'

export function useCollaborationAccount(enabled: boolean) {
  const [server, setServer] = useState(() => localStorage.getItem(preference) ?? '')
  const [account, setAccount] = useState<Account | null>(null)
  const [configuration, setConfiguration] = useState<Configuration | null>(null)
  const [busy, setBusy] = useState<'checking' | 'connecting' | 'signIn' | 'signOut' | null>(null)
  const [error, setError] = useState('')
  const [retry, setRetry] = useState(0)
  const generation = useRef(0)
  const active = useRef(false)
  const savedServer = useHostedSessionStore(state => state.saved?.server)
  useEffect(() => {
    if (savedServer) setServer(current => current || savedServer)
  }, [savedServer])
  useEffect(() => {
    if (!enabled || !server.trim()) return
    const current = ++generation.current
    const timer = setTimeout(() => {
      if (generation.current !== current || active.current) return
      setBusy('checking')
      void (async () => {
        try {
          const value = await rpc.request<Configuration>('collaboration/account/configuration', [server.trim()])
          if (generation.current !== current) return
          setConfiguration(value)
          const restored = value.authenticationRequired
            ? await rpc.request<Account | null>('collaboration/account/status', [server.trim()]) : null
          if (generation.current === current) { setAccount(restored); setError('') }
        } catch (reason) { if (generation.current === current) setError(String(reason)) }
        finally { if (generation.current === current) setBusy(null) }
      })()
    }, 350)
    return () => { clearTimeout(timer); generation.current++ }
  }, [server, enabled, retry])
  const signIn = async (address = server.trim()): Promise<boolean> => {
    if (active.current) return false
    active.current = true
    generation.current++
    setError(''); setBusy('connecting')
    try {
      const policy = await rpc.request<Configuration>('collaboration/account/configuration', [address])
      setConfiguration(policy)
      let value: Account | null = null
      if (policy.authenticationRequired) {
        value = await rpc.request<Account | null>('collaboration/account/status', [address])
        if (!value) {
          setAccount(null)
          setBusy('signIn')
          value = await rpc.request<Account>('collaboration/account/signIn', [address])
        }
      }
      localStorage.setItem(preference, address)
      setServer(address); setAccount(value)
      return true
    } catch (reason) { setError(String(reason)); return false }
    finally { active.current = false; setBusy(null) }
  }
  const signOut = async (): Promise<void> => {
    generation.current++
    setError(''); setBusy('signOut')
    try {
      await rpc.request('collaboration/account/signOut', [server.trim()])
      setAccount(null)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(null) }
  }
  const changeServer = (value: string): void => {
    generation.current++
    setServer(value); setAccount(null); setConfiguration(null); setError(''); setBusy(null)
  }
  const cancel = async (): Promise<void> => {
    try { await rpc.request('collaboration/account/cancel') }
    catch (reason) { setError(String(reason)) }
  }
  return { server, account, configuration, busy, error, signIn, signOut, changeServer, cancel, retry: () => setRetry(value => value + 1) }
}
