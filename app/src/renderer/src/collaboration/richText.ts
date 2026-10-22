import type * as Y from 'yjs'
import { diffChars } from 'diff'

export interface TextRun {
  insert: string
  attributes?: Record<string, string>
}

export function updateRichText(text: Y.Text, runs: TextRun[]): void {
  const previous = plainText(text)
  const next = runs.map(run => run.insert).join('')
  let position = 0
  for (const change of diffChars(previous, next)) {
    if (change.removed) text.delete(position, change.value.length)
    else {
      if (change.added) text.insert(position, change.value)
      position += change.value.length
    }
  }
  const current: TextRun[] = text.toDelta()
  let offset = 0
  for (const run of runs) {
    updateMarks(text, current, offset, run)
    offset += run.insert.length
  }
}

function updateMarks(text: Y.Text, current: TextRun[], offset: number, run: TextRun): void {
  let position = 0
  for (const existing of current) {
    const start = Math.max(offset, position)
    const end = Math.min(offset + run.insert.length, position + existing.insert.length)
    position += existing.insert.length
    if (end <= start) continue
    const changes: Record<string, string | null> = {}
    for (const key of new Set([...Object.keys(existing.attributes ?? {}), ...Object.keys(run.attributes ?? {})])) {
      const desired = run.attributes?.[key] ?? null
      if ((existing.attributes?.[key] ?? null) !== desired) changes[key] = desired
    }
    if (Object.keys(changes).length) text.format(start, end - start, changes)
  }
}

export function plainText(text: Y.Text): string {
  const runs: TextRun[] = text.toDelta()
  return runs.map(run => run.insert).join('')
}

export function renderRichText(document: Document, runs: TextRun[]): DocumentFragment {
  const fragment = document.createDocumentFragment()
  for (const run of runs) {
    let node: Node = run.attributes?.embed ? renderEmbed(document, run.attributes.embed, run.insert.length) : document.createTextNode(run.insert)
    for (const key of orderedMarks(run.attributes ?? {}).reverse()) {
      const tag = key.split(':')[0]
      if (!/^[a-z][a-z0-9]*$/.test(tag)) throw new Error('Invalid shared formatting tag.')
      const element = document.createElement(tag)
      const attributes: unknown = JSON.parse(run.attributes![key])
      if (!attributes || typeof attributes !== 'object' || Array.isArray(attributes)) throw new Error('Invalid shared formatting attributes.')
      for (const [name, value] of Object.entries(attributes)) {
        if (typeof value !== 'string') throw new Error('Invalid shared formatting attribute.')
        element.setAttribute(name, value)
      }
      element.append(node)
      node = element
    }
    fragment.append(node)
  }
  return fragment
}

function orderedMarks(attributes: Record<string, string>): string[] {
  const keys = Object.keys(attributes).filter(key => key !== 'embed' && key !== 'markOrder')
  const order: unknown = attributes.markOrder ? JSON.parse(attributes.markOrder) : []
  if (!Array.isArray(order) || !order.every(key => typeof key === 'string')) throw new Error('Invalid shared formatting order.')
  const ordered = order.filter((key: string) => keys.includes(key))
  return [...new Set([...ordered, ...keys.filter(key => !ordered.includes(key)).sort()])]
}

function renderEmbed(document: Document, value: string, count: number): DocumentFragment {
  const embed: unknown = JSON.parse(value)
  if (!embed || typeof embed !== 'object' || !('tag' in embed) || typeof embed.tag !== 'string' || !['img', 'br', 'hr'].includes(embed.tag) || !('attributes' in embed) || !embed.attributes || typeof embed.attributes !== 'object') throw new Error('Invalid shared inline object.')
  const fragment = document.createDocumentFragment()
  for (let index = 0; index < count; index++) {
    const element = document.createElement(embed.tag)
    for (const [name, value] of Object.entries(embed.attributes)) {
      if (typeof value !== 'string') throw new Error('Invalid shared inline attribute.')
      element.setAttribute(name, value)
    }
    fragment.append(element)
  }
  return fragment
}
