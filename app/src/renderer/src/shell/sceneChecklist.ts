export interface ChecklistText { kind: 'text'; text: string; line: number }
export interface ChecklistItem {
  kind: 'item'
  title: string
  done: boolean
  line: number
  children: ChecklistBlock[]
}
export type ChecklistBlock = ChecklistText | ChecklistItem

const BULLET = /^([\t ]*)([-+*•]|\d+[.)])([\t ]+)(?:\[([ xX])\]([\t ]*))?(.*)$/
const RULE = /^\s*(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$/

/** Split without discarding CRLF or a final newline. All mutations target only
 * the list marker, never reserialize the writer's prose. */
function lines(text: string): string[] { return text.split(/(?<=\n)/) }
function content(line: string): string { return line.replace(/\r?\n$/, '') }
function indent(text: string): number { return text.replace(/\t/g, '    ').length }

function listLines(text: string, fencedContent = new Set<number>()): Map<number, RegExpMatchArray> {
  const matches = new Map<number, RegExpMatchArray>()
  let fence: { char: string; length: number } | null = null
  lines(text).forEach((raw, index) => {
    const line = content(raw)
    if (fence) fencedContent.add(index)
    const mark = line.match(/^\s*(`{3,}|~{3,})(.*)$/)
    if (mark) {
      if (!fence) fence = { char: mark[1][0], length: mark[1].length }
      else if (mark[1][0] === fence.char && mark[1].length >= fence.length && !mark[2].trim()) fence = null
      return
    }
    if (fence || RULE.test(line)) return
    const match = line.match(BULLET)
    if (match) matches.set(index, match)
  })
  return matches
}

export function normalizeChecklist(text: string): string {
  const matches = listLines(text)
  return lines(text).map((line, index) => {
    const match = matches.get(index)
    if (!match || match[4] !== undefined) return line
    const offset = match[1].length + match[2].length + match[3].length
    return line.slice(0, offset) + '[ ] ' + line.slice(offset)
  }).join('')
}

export function parseChecklist(text: string): ChecklistBlock[] {
  const result: ChecklistBlock[] = []
  const stack: { indent: number; item: ChecklistItem }[] = []
  const fencedContent = new Set<number>()
  const matches = listLines(text, fencedContent)
  lines(text).forEach((raw, line) => {
    const match = matches.get(line)
    if (match) {
      const depth = indent(match[1])
      while (stack.length && stack[stack.length - 1].indent >= depth) stack.pop()
      const item: ChecklistItem = { kind: 'item', title: match[6], done: match[4]?.toLowerCase() === 'x', line, children: [] }
      const parent = stack[stack.length - 1]?.item.children ?? result
      parent.push(item)
      stack.push({ indent: depth, item })
    } else {
      const value = content(raw)
      if (value.trim() && !fencedContent.has(line)) {
        const depth = indent(value.match(/^[\t ]*/)?.[0] ?? '')
        // Unindented continuation belongs to the root; indented prose belongs
        // to the nearest containing item. A new heading starts a free section.
        if (/^#{1,6}\s/.test(value)) stack.length = 0
        else while (stack.length > 1 && stack[stack.length - 1].indent >= depth) stack.pop()
      }
      const parent = stack[stack.length - 1]?.item.children ?? result
      const last = parent[parent.length - 1]
      if (last?.kind === 'text') last.text += raw
      else parent.push({ kind: 'text', text: raw, line })
    }
  })
  return result
}

export function setChecklistDone(text: string, line: number, done: boolean): string {
  const normalized = normalizeChecklist(text)
  const match = listLines(normalized).get(line)
  if (!match) return text
  const parts = lines(normalized)
  const offset = match[1].length + match[2].length + match[3].length + 1
  parts[line] = parts[line].slice(0, offset) + (done ? 'x' : ' ') + parts[line].slice(offset + 1)
  return parts.join('')
}

export function checklistProgress(blocks: ChecklistBlock[]): { done: number; total: number } {
  let done = 0
  let total = 0
  for (const block of blocks) if (block.kind === 'item') {
    total++
    if (block.done) done++
    const children = checklistProgress(block.children)
    done += children.done
    total += children.total
  }
  return { done, total }
}

/** CodeMirror emits LF text; keep the note's on-disk newline convention. */
export function preserveChecklistLineEndings(previous: string, edited: string): string {
  return previous.includes('\r\n') ? edited.replace(/\r?\n/g, '\r\n') : edited
}
