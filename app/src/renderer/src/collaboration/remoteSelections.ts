import css from './presence.css?inline'

export interface SharedSelection {
  anchor: number[]
  focus: number[]
}

export interface ParticipantSelection {
  id: string
  name: string
  selection: SharedSelection
}

export class RemoteSelections {
  private readonly participants = new Map<string, ParticipantSelection>()
  private readonly markers = new Map<string, HTMLElement>()
  private readonly style: HTMLStyleElement
  private readonly document: Document
  private readonly highlight = `novalist-collaborators-${crypto.randomUUID()}`

  constructor(private readonly editor: Window, private readonly range: (selection: SharedSelection) => Range | null) {
    this.document = editor.document
    this.style = this.document.createElement('style')
    this.style.textContent = css.replaceAll('novalist-collaborators', this.highlight)
    this.document.head.append(this.style)
    this.document.addEventListener('scroll', this.render, true)
    this.editor.addEventListener('resize', this.render)
  }

  show(participant: ParticipantSelection): void {
    this.participants.set(participant.id, participant)
    this.render()
  }

  remove(id: string): void {
    this.participants.delete(id)
    this.markers.get(id)?.remove()
    this.markers.delete(id)
    this.render()
  }

  readonly render = (): void => {
    const ranges: Range[] = []
    for (const participant of this.participants.values()) {
      const range = this.range(participant.selection)
      const caret = this.range({ anchor: participant.selection.focus, focus: participant.selection.focus })
      const marker = this.markers.get(participant.id) ?? this.createMarker(participant)
      marker.hidden = !range || !caret
      if (!range || !caret) continue
      ranges.push(range)
      let bounds = caret.getBoundingClientRect()
      if (!bounds.height) {
        const container = caret.startContainer.nodeType === Node.ELEMENT_NODE ? caret.startContainer as Element : caret.startContainer.parentElement
        if (container) bounds = container.getBoundingClientRect()
      }
      marker.style.left = `${bounds.left}px`
      marker.style.top = `${bounds.top}px`
      marker.style.height = `${bounds.height}px`
      marker.firstChild!.textContent = participant.name
    }
    const view = this.editor as Window & typeof globalThis
    view.CSS.highlights.set(this.highlight, new view.Highlight(...ranges))
  }

  private createMarker(participant: ParticipantSelection): HTMLElement {
    const marker = this.document.createElement('div')
    marker.className = 'nv-collaboration-caret'
    marker.setAttribute('aria-hidden', 'true')
    const label = this.document.createElement('span')
    label.className = 'nv-collaboration-label'
    label.textContent = participant.name
    marker.append(label)
    this.document.body.append(marker)
    this.markers.set(participant.id, marker)
    return marker
  }

  destroy(): void {
    this.document.removeEventListener('scroll', this.render, true)
    this.editor.removeEventListener('resize', this.render)
    for (const marker of this.markers.values()) marker.remove()
    this.style.remove()
    ;(this.editor as Window & typeof globalThis).CSS.highlights.delete(this.highlight)
  }
}
