import { useEffect, useState } from 'react'
import { rpc } from '../../rpc/client'
import { registerBackgroundRead } from '../../rpc/backgroundReads'

export function useDashboardRead<T>(method: string | null, revision?: string): T | null {
  const [data, setData] = useState<T | null>(null)
  useEffect(() => {
    if (!method) return
    const controller = new AbortController()
    const unregister = registerBackgroundRead(controller)
    void rpc.request<T>(method, undefined, { signal: controller.signal })
      .then(next => { if (!controller.signal.aborted) setData(next) })
      .catch(() => { if (!controller.signal.aborted) setData(null) })
      .finally(unregister)
    return () => { unregister(); controller.abort() }
  }, [method, revision])
  return data
}
