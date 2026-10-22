import { rpc } from '../rpc/client'
import type { FieldBinding } from './FocusedField'
import { SharedMarkdownSession } from './SharedMarkdownSession'
import { subscribeSharedDocument } from './sharedEditors'

export interface SharedEntityProse { sessionId: string; fields: Record<string, string> }

export function proseBinding(record: Record<string, unknown> | null, key: string): FieldBinding | undefined {
  const prose = record?.sharedProse as SharedEntityProse | undefined
  const itemId = prose?.fields[key]
  return prose && itemId ? { sessionId: prose.sessionId, itemId } : undefined
}

export async function openSharedMarkdown(binding: FieldBinding, changed: (failed: boolean) => void): Promise<SharedMarkdownSession> {
  const read = async (): Promise<Uint8Array> => decode(await rpc.request<string>('collaboration/readScene', [binding.sessionId, binding.itemId]))
  const initial = await read()
  return new SharedMarkdownSession(initial, {
    read,
    write: update => rpc.request('collaboration/writeScene', [binding.sessionId, binding.itemId, encode(update)]),
    subscribe: receive => subscribeSharedDocument(binding, receive)
  }, changed)
}

function decode(value: string): Uint8Array {
  if (value.length > Math.ceil(64 * 1024 * 1024 / 3) * 4) throw new Error('Shared prose is too large.')
  return Uint8Array.from(atob(value), character => character.charCodeAt(0))
}

function encode(value: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < value.length; offset += 8192) binary += String.fromCharCode(...value.subarray(offset, offset + 8192))
  return btoa(binary)
}
