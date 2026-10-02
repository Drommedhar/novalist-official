import type { RpcClient } from '../rpc/client'

export interface DictationOptions {
  formattingMode: 'automatic' | 'plain'
  voiceCommands: boolean
  microphoneId: string
}
const OPTIONS_KEY = 'novalist.dictation.options'
export function readDictationOptions(): DictationOptions {
  try {
    const value = JSON.parse(localStorage.getItem(OPTIONS_KEY) ?? '{}')
    return { formattingMode: value.formattingMode === 'plain' ? 'plain' : 'automatic',
      voiceCommands: value.voiceCommands === true, microphoneId: typeof value.microphoneId === 'string' ? value.microphoneId : '' }
  } catch { return { formattingMode: 'automatic', voiceCommands: false, microphoneId: '' } }
}
export function saveDictationOptions(options: DictationOptions): void {
  try { localStorage.setItem(OPTIONS_KEY, JSON.stringify(options)) } catch { /* Storage can be unavailable. */ }
}
/** Collect names and aliases from summaries; recognition never receives descriptions or manuscript prose. */
export async function codexVocabulary(request: RpcClient['request']): Promise<string[]> {
  const customTypes = await request<{ typeKey: string }[]>('entities/customTypes')
  const lists = await Promise.all(['character', 'location', 'item', 'lore', ...customTypes.map(type => type.typeKey)]
    .map(type => request<{ name: string; aliases: string[]; firstName?: string; surname?: string }[]>('entities/list', [type])))
  return lists.flatMap(list => list.flatMap(entry => [entry.name, entry.firstName ?? '', entry.surname ?? '', ...(entry.aliases ?? [])]))
}
export function codexTerms(names: string[]): string[] {
  const terms: string[] = []
  const known = new Set<string>()
  let size = 0
  for (const name of names) {
    const term = name.trim().replace(/\s+/g, ' ').normalize()
    if (!term || known.has(term.toLocaleLowerCase())) continue
    known.add(term.toLocaleLowerCase())
    if (terms.length >= 128 || term.length > 80 || /[\x00-\x1f\x7f-\x9f]/.test(term) || size + term.length + 2 > 2000) continue
    terms.push(term)
    size += term.length + 2
  }
  return terms
}

export type DictationPart = { kind: 'text'; text: string } | { kind: 'paragraph' | 'speaker' }
/** Commands must occupy a complete sentence/clip. Preserve punctuation on preceding prose. */
export function splitDictationCommands(transcript: string, enabled: boolean): DictationPart[] {
  if (!enabled) return [{ kind: 'text', text: transcript }]
  const commands = /(^|[.!?\n]\s*)(new paragraph|new speaker|neuer absatz|neuer sprecher|neue sprecherin)(?=\s*(?:[.!?\n]|$))/giu
  const parts: DictationPart[] = []
  let offset = 0
  for (const match of transcript.matchAll(commands)) {
    const start = match.index + match[1].length
    const text = transcript.slice(offset, start).trim()
    if (text) parts.push({ kind: 'text', text })
    const command = match[2].toLowerCase()
    parts.push({ kind: command === 'new paragraph' || command === 'neuer absatz' ? 'paragraph' : 'speaker' })
    offset = start + match[2].length
    offset += transcript.slice(offset).match(/^[\t ]*[.!?]+/)?.[0].length ?? 0
  }
  const text = transcript.slice(offset).trim()
  if (text) parts.push({ kind: 'text', text })
  return parts
}
