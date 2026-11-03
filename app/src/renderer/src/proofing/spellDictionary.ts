import { rpc } from '../rpc/client'
import { registerBackgroundRead } from '../rpc/backgroundReads'

export async function loadSpellDictionary(controller = new AbortController()): Promise<string[]> {
  const unregister = registerBackgroundRead(controller)
  try {
    return await rpc.request<string[]>('spell/dictionary', undefined, { signal: controller.signal })
  } finally {
    unregister()
  }
}
