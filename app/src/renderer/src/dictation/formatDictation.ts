import type { ReplacementRule } from '../views/settings/AutoReplacementsCard'

export interface DictationSegment {
  text: string
  kind: 'narration' | 'dialogue' | 'attribution'
  newParagraph: boolean
}
export interface QuotePair { open: string; close: string }

export function dictationQuotes(language: string, rules: ReplacementRule[]): QuotePair {
  const custom = rules.find((r) => r.kind !== 'regex' && (r.start === "'" || r.start === '"')
    && r.start === r.end && r.startReplace && r.endReplace)
  if (custom) return { open: custom.startReplace, close: custom.endReplace }
  if (['de-low', 'pl', 'cs', 'sk'].includes(language)) return { open: '„', close: '“' }
  if (language === 'de-guillemet') return { open: '»', close: '«' }
  if (language === 'fr') return { open: '«\u00a0', close: '\u00a0»' }
  if (['es', 'it', 'pt', 'ru'].includes(language)) return { open: '«', close: '»' }
  return { open: '“', close: '”' }
}

export interface DictationInsertion {
  text: string
  paragraph: boolean
  /** Join two chunks of the same speech by removing only our closing quote. */
  mergeClose: string
  tag?: { close: string; language: string }
  lastKind: DictationSegment['kind']
}

export function formatDictation(segments: DictationSegment[], quotes: QuotePair, language: string,
  previousKind?: DictationSegment['kind']): DictationInsertion {
  const first = segments[0]
  const paragraph = !!first && (first.newParagraph || (first.kind === 'dialogue'
    && previousKind !== 'dialogue' && previousKind !== 'attribution'))
  const mergeClose = first?.kind === 'dialogue' && previousKind === 'dialogue'
    && !first.newParagraph ? quotes.close : ''
  let text = ''
  let previous = previousKind
  segments.forEach((segment, index) => {
    let part = segment.text.trim()
    if (!part) return
    const next = segments[index + 1]
    if (segment.kind === 'dialogue') {
      // Providers occasionally include the outer quotes despite the contract.
      part = part.replace(/^["„“”»«]+\s*/, '').replace(/\s*["„“”»«]+$/, '')
      let after = ''
      if (next?.kind === 'attribution' && !next.newParagraph) {
        if (language === 'de') {
          part = part.replace(/[.,]$/, '')
          after = ','
        } else if (!/[?!,]$/.test(part)) part = part.replace(/\.$/, '') + ','
      }
      part = (index === 0 && mergeClose ? '' : quotes.open) + part + quotes.close + after
    }
    const breakLine = index > 0 && (segment.newParagraph
      || (segment.kind === 'dialogue' && previous === 'narration')
      || (segment.kind === 'narration' && previous !== 'narration'))
    text += (text ? breakLine ? '\n' : /^[,.;:!?]/.test(part) ? '' : ' ' : '') + part
    previous = segment.kind
  })
  return { text, paragraph, mergeClose, lastKind: previous ?? 'narration',
    ...(first?.kind === 'attribution' && previousKind === 'dialogue' && !first.newParagraph
      ? { tag: { close: quotes.close, language } } : {}) }
}
