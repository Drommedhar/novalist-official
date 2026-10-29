import { useEffect, useRef, useState } from 'react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

export function useEntityFieldSuggestions(entityType: string, recordId: unknown) {
  const scope = useProjectStore(state => JSON.stringify([state.projectPath, state.activeBookId,
    state.activeDraftId, state.workspaceEpoch, entityType, recordId]))
  const requests = useRef(new Map<string, AbortController>())
  const [values, setValues] = useState({ scope, names: {} as Record<string, string[]>, errors: {} as Record<string, string> })
  useEffect(() => {
    const active = requests.current
    return () => { for (const request of active.values()) request.abort(); active.clear() }
  }, [scope])

  const loadRequest = (key: string, read: (options: { signal: AbortSignal }) => Promise<Record<string, string[]>>): void => {
    if (requests.current.has(key)) return
    const controller = new AbortController()
    requests.current.set(key, controller)
    void read({ signal: controller.signal }).then(names => {
      if (controller.signal.aborted) return
      setValues(current => ({ scope, names: { ...(current.scope === scope ? current.names : {}), ...names },
        errors: current.scope === scope ? { ...current.errors, [key]: '' } : {} }))
    }).catch(error => {
      if (controller.signal.aborted) return
      requests.current.delete(key)
      setValues(current => ({ scope, names: current.scope === scope ? current.names : {},
        errors: { ...(current.scope === scope ? current.errors : {}), [key]: String(error) } }))
    })
  }
  const load = (type: string | null): void => loadRequest(type ?? '', async options => {
    const names = type === null
      ? await rpc.request<string[]>('entities/groups', undefined, options)
      : [...new Set((await rpc.request<{ name: string }[]>('entities/list', [type], options)).map(entry => entry.name))].sort()
    return { [type ?? '']: names }
  })
  const loadRelationships = (): void => loadRequest('relationships', async options => {
    const result = await rpc.request<{ characterNames: string[]; roles: string[] }>('entities/relationshipSuggestions', undefined, options)
    return { relationshipNames: result.characterNames, relationshipRoles: result.roles }
  })
  return { load, loadRelationships, names: values.scope === scope ? values.names : {}, errors: values.scope === scope ? values.errors : {} }
}
