import { renderRichText, type TextRun } from './richText'

export interface MarkupEnvironment {
  document: Document
  sanitizeSceneFragment(html: string): DocumentFragment
  toDisplayImages(root: ParentNode): void
  refreshLayout?(): void
}

interface Block {
  id: string
  tag: string
  attributes: Record<string, string>
}

interface NodePosition {
  start: number
  offsets: number[]
}

const blockTags = new Set(['p', 'div', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'blockquote', 'pre', 'ul', 'ol', 'li', 'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td', 'figure', 'figcaption'])

export class SceneMarkup {
  private identities = new WeakMap<Node, string>()
  private positions = new WeakMap<Node, NodePosition>()
  private textNodes: { node: Node; start: number; length: number }[] = []
  private boundaries: { node: Node; offsets: number[] }[] = []
  private runs: TextRun[] = []
  private offset = 0

  constructor(private readonly editor: MarkupEnvironment, private readonly root: HTMLElement) {}

  read(): TextRun[] {
    this.runs = []
    this.offset = 0
    this.textNodes = []
    this.boundaries = []
    this.positions = new WeakMap()
    this.container(this.root, [])
    return this.runs
  }

  private append(insert: string, attributes: Record<string, string> = {}): void {
    if (insert) this.runs.push({ insert, attributes })
    this.offset += insert.length
  }

  private block(element: Element): Block {
    let id = this.identities.get(element)
    if (!id) {
      id = crypto.randomUUID()
      this.identities.set(element, id)
    }
    return { id, tag: element.tagName.toLowerCase(), attributes: Object.fromEntries(Array.from(element.attributes, attribute => [attribute.name, attribute.value])) }
  }

  private container(element: Element, path: Block[]): void {
    const position: NodePosition = { start: this.offset, offsets: [] }
    this.positions.set(element, position)
    let inline = false
    let nested = false
    let continuation = false
    const finish = (): void => {
      this.append('\n', { block: JSON.stringify(path), ...(continuation ? { continuation: 'true' } : {}) })
      continuation = true
      inline = false
    }
    for (const node of element.childNodes) {
      if (node.nodeType === this.root.ELEMENT_NODE && this.isBlock(node as Element)) {
        if (inline) finish()
        position.offsets.push(this.offset)
        const child = node as Element
        this.container(child, child.classList.contains('nv-page') ? path : [...path, this.block(child)])
        nested = true
      } else {
        position.offsets.push(this.offset)
        this.inline(node, {})
        inline = true
      }
    }
    position.offsets.push(this.offset)
    this.boundaries.push({ node: element, offsets: position.offsets })
    if (inline || (!nested && path.length > 0)) finish()
  }

  private isBlock(element: Element): boolean {
    return blockTags.has(element.tagName.toLowerCase())
  }

  private inline(node: Node, attributes: Record<string, string>): void {
    const position: NodePosition = { start: this.offset, offsets: [] }
    this.positions.set(node, position)
    if (node.nodeType === this.root.TEXT_NODE) {
      const text = node.textContent ?? ''
      this.textNodes.push({ node, start: this.offset, length: text.length })
      this.append(text, attributes)
      return
    }
    if (node.nodeType !== this.root.ELEMENT_NODE) return
    const element = node as Element
    const tag = element.tagName.toLowerCase()
    const values = Object.fromEntries(Array.from(element.attributes, attribute => [attribute.name, attribute.value]))
    if (tag === 'img' && values['data-nv-src']) {
      values.src = values['data-nv-src']
      delete values['data-nv-src']
    }
    if (['img', 'br', 'hr'].includes(tag)) {
      this.append('\ufffc', { ...attributes, embed: JSON.stringify({ tag, attributes: values }) })
      return
    }
    const prefix = `${tag}:${values.class ?? ''}:`
    const key = `${prefix}${Object.keys(attributes).filter(key => key.startsWith(prefix)).length}`
    const order = [...Object.keys(attributes).filter(name => name !== 'markOrder'), key]
    const marks = element.classList.contains('grammar-issue') ? attributes : { ...attributes, [key]: JSON.stringify(values), markOrder: JSON.stringify(order) }
    for (const child of node.childNodes) {
      position.offsets.push(this.offset)
      this.inline(child, marks)
    }
    position.offsets.push(this.offset)
    this.boundaries.push({ node: element, offsets: position.offsets })
  }

  position(node: Node, offset: number): number | null {
    const position = this.positions.get(node)
    if (!position) return null
    return node.nodeType === this.root.TEXT_NODE ? position.start + offset : position.offsets[offset] ?? null
  }

  domPosition(index: number): { node: Node; offset: number } | null {
    for (const entry of this.textNodes) {
      if (index >= entry.start && index <= entry.start + entry.length) return { node: entry.node, offset: index - entry.start }
    }
    for (const entry of this.boundaries) {
      const offset = entry.offsets.indexOf(index)
      if (offset !== -1) return { node: entry.node, offset }
    }
    return null
  }

  render(runs: TextRun[]): void {
    const fragment = this.prepare(runs)
    this.patch(this.root, fragment)
    this.editor.toDisplayImages(this.root)
    this.editor.refreshLayout?.()
    this.read()
  }

  validateAttribute(name: string, value: string): void {
    const document = this.editor.document.implementation.createHTMLDocument()
    if (name === 'block') {
      for (const block of this.parsePath(value)) this.createBlock(document, block)
    } else if (name === 'continuation') {
      if (value !== 'true') throw new Error('Invalid shared paragraph continuation.')
    } else {
      renderRichText(document, [{ insert: '\ufffc', attributes: { [name]: value } }])
    }
  }

  prepare(runs: TextRun[]): DocumentFragment {
    for (const run of runs) {
      if (typeof run.insert !== 'string' || (run.attributes && Object.values(run.attributes).some(value => typeof value !== 'string')))
        throw new Error('Invalid shared prose value.')
      if (run.attributes?.block && !/^\n+$/.test(run.insert)) throw new Error('Invalid shared paragraph boundary.')
    }
    const document = this.editor.document.implementation.createHTMLDocument()
    const fragment = document.createDocumentFragment()
    const blocks = new Map<string, Element>()
    const completed = new Set<Element>()
    let content: TextRun[] = []
    const paragraphs = runs.flatMap(run => run.attributes?.block && /^\n+$/.test(run.insert)
      ? Array.from(run.insert, insert => ({ insert, attributes: run.attributes })) : [run])
    for (const run of paragraphs) {
      if (run.attributes?.block && run.insert === '\n') {
        const path = this.parsePath(run.attributes.block)
        let parent: Element | DocumentFragment = fragment
        for (const block of path) {
          let element = blocks.get(block.id)
          if (!element || (block === path.at(-1) && completed.has(element) && run.attributes.continuation !== 'true')) {
            element = this.createBlock(document, block)
            blocks.set(block.id, element)
            parent.append(element)
          }
          parent = element
        }
        const rendered = document.createElement('div')
        rendered.append(renderRichText(document, content))
        parent.append(this.editor.sanitizeSceneFragment(rendered.innerHTML))
        if (parent.nodeType === this.root.ELEMENT_NODE) completed.add(parent as Element)
        content = []
      } else {
        content.push(run)
      }
    }
    if (content.length) throw new Error('Shared prose has no final paragraph boundary.')
    return fragment
  }

  private parsePath(value: string): Block[] {
    const path: unknown = JSON.parse(value)
    if (!Array.isArray(path)) throw new Error('Invalid shared paragraph path.')
    return path.map((block: unknown) => {
      if (!block || typeof block !== 'object' || !('id' in block) || typeof block.id !== 'string' || !('tag' in block) || typeof block.tag !== 'string' || !blockTags.has(block.tag) || !('attributes' in block) || !block.attributes || typeof block.attributes !== 'object' || Array.isArray(block.attributes)) throw new Error('Invalid shared paragraph.')
      const attributes: Record<string, string> = {}
      for (const [name, value] of Object.entries(block.attributes)) {
        if (typeof value !== 'string') throw new Error('Invalid shared paragraph attribute.')
        attributes[name] = value
      }
      return { id: block.id, tag: block.tag, attributes }
    })
  }

  private createBlock(document: Document, block: Block): Element {
    const element = document.createElement(block.tag)
    for (const [name, value] of Object.entries(block.attributes)) element.setAttribute(name, value)
    const safe = this.editor.sanitizeSceneFragment(element.outerHTML).firstElementChild
    if (!safe) throw new Error('Unsupported shared paragraph.')
    this.identities.set(safe, block.id)
    return safe
  }

  private patch(parent: Element, desired: Element | DocumentFragment): void {
    const previous = Array.from(parent.childNodes)
    const used = new Set<Node>()
    let index = 0
    for (const target of Array.from(desired.childNodes)) {
      const id = this.identities.get(target)
      const existing = id ? previous.find(node => !used.has(node) && this.identities.get(node) === id && node.nodeName === target.nodeName) : previous[index]
      let node = target
      if (existing?.nodeType === this.root.ELEMENT_NODE && target.nodeType === this.root.ELEMENT_NODE && existing.nodeName === target.nodeName) {
        const element = existing as Element
        const next = target as Element
        for (const attribute of Array.from(element.attributes)) if (!next.hasAttribute(attribute.name)) element.removeAttribute(attribute.name)
        for (const attribute of Array.from(next.attributes)) element.setAttribute(attribute.name, attribute.value)
        if (id) this.identities.set(element, id)
        this.patch(element, next)
        node = element
      } else if (existing?.nodeType === this.root.TEXT_NODE && target.nodeType === this.root.TEXT_NODE) {
        if (existing.textContent !== target.textContent) existing.textContent = target.textContent
        node = existing
      }
      if (parent.childNodes[index] !== node) parent.insertBefore(node, parent.childNodes[index] ?? null)
      used.add(node)
      index++
    }
    while (parent.childNodes.length > index) parent.lastChild?.remove()
  }
}
