import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CheckCheck, ListChecks, Pencil, Plus } from 'lucide-react'
import Markdown from 'react-markdown'
import type { SceneNotesContext } from '../../../shared/sceneNotesProtocol'
import { MarkdownEditor } from './MarkdownEditor'
import { checklistProgress, normalizeChecklist, parseChecklist, preserveChecklistLineEndings, setChecklistDone, type ChecklistBlock } from './sceneChecklist'
import { useSceneNotesDocument } from './useSceneNotesDocument'
import { checklistExpansionScope, checklistItemKeys, readChecklistExpansion, saveChecklistExpansion } from './checklistExpansion'

interface DisclosureState {
  keys: Map<number, string>
  expanded: Set<string>
  toggle(key: string, open: boolean): void
}

function ChecklistBlocks({ blocks, onCheck, disclosure }: { blocks: ChecklistBlock[]; onCheck: (line: number, done: boolean) => void; disclosure: DisclosureState }): React.JSX.Element {
  const { t } = useTranslation()
  return <>{blocks.map((block) => {
    if (block.kind === 'text') return block.text.trim()
      ? <div key={block.line} className="scene-checklist-prose"><Markdown skipHtml>{block.text.replace(/^[\t ]+/gm, '')}</Markdown></div> : null
    const hasDetails = block.children.some((child) => child.kind === 'item' || child.text.trim())
    const progress = checklistProgress(block.children)
    const key = disclosure.keys.get(block.line)!
    const title = <span id={`note-title-${block.line}`} className="scene-checklist-item-title">{block.title || t('sceneChecklist.untitled')}</span>
    return <div key={block.line} className={`scene-checklist-item${block.done ? ' is-done' : ''}`}>
      <input type="checkbox" aria-labelledby={`note-title-${block.line}`} checked={block.done} onChange={(event) => onCheck(block.line, event.target.checked)} />
      {hasDetails ? <details className="scene-checklist-details" open={disclosure.expanded.has(key)} onToggle={(event) => { if (event.target === event.currentTarget) disclosure.toggle(key, event.currentTarget.open) }}>
        <summary onClick={(event) => { event.preventDefault(); disclosure.toggle(key, !disclosure.expanded.has(key)) }}>{title}{progress.total > 0 && <span className="scene-checklist-count">{progress.done}/{progress.total}</span>}</summary>
        <div className="scene-checklist-children"><ChecklistBlocks blocks={block.children} onCheck={onCheck} disclosure={disclosure} /></div>
      </details> : title}
    </div>
  })}</>
}

export function SceneChecklistView({ context }: { context: SceneNotesContext }): React.JSX.Element {
  const { t } = useTranslation()
  const { document, state, shared, markdownSession } = useSceneNotesDocument(context)
  const [editing, setEditing] = useState(false)
  const [newItem, setNewItem] = useState('')
  const expansionScope = checklistExpansionScope(context)
  const [expanded, setExpanded] = useState(() => readChecklistExpansion(expansionScope))
  const expandedRef = useRef(expanded)
  const blocks = parseChecklist(state.text)
  const disclosure: DisclosureState = {
    keys: checklistItemKeys(blocks), expanded,
    toggle: (key, open) => {
      const previous = expandedRef.current
      if (previous.has(key) === open) return
      const next = new Set(previous)
      if (open) next.add(key)
      else next.delete(key)
      expandedRef.current = next
      // Native toggle events are queued. Persist in the summary's click
      // handler too, before an immediate close or reload can cancel them.
      saveChecklistExpansion(expansionScope, next)
      setExpanded(next)
    }
  }
  const completed = blocks.filter((block) => block.kind === 'item' && block.done)
  const remaining = blocks.filter((block) => block.kind !== 'item' || !block.done)
  const progress = checklistProgress(blocks)
  const save = (): void => { void document.flush().catch(() => {}) }
  const finishEditing = (): void => {
    if (!shared) document.update(normalizeChecklist(document.state.text))
    save()
    setEditing(false)
  }
  const check = (line: number, done: boolean): void => {
    const text = document.state.text
    // An incoming edit can insert lines before React commits its next render.
    // Resolve the rendered item again instead of checking a different row.
    const key = disclosure.keys.get(line)
    const currentLine = text === state.text ? line
      : [...checklistItemKeys(parseChecklist(text))].find(([, value]) => value === key)?.[0]
    if (currentLine === undefined) return
    const checked = setChecklistDone(text, currentLine, done)
    // A shared checkbox changes its own marker only. Normalizing every other
    // list can race a peer converting those same plain bullets to checkboxes.
    if (shared) {
      const lines = text.split(/(?<=\n)/)
      lines[currentLine] = checked.split(/(?<=\n)/)[currentLine]
      document.update(lines.join(''))
    } else document.update(checked)
    save()
  }
  const add = (): void => {
    if (!newItem.trim()) return
    const text = document.state.text
    const newline = text.includes('\r\n') ? '\r\n' : '\n'
    const next = text + (text && !text.endsWith('\n') ? newline : '') + '- [ ] ' + newItem.trim() + newline
    document.update(shared ? next : normalizeChecklist(next))
    setNewItem('')
    save()
  }

  return <section className={`scene-checklist${editing ? ' is-editing' : ''}`} aria-label={t('sceneChecklist.title')}>
    <div className="scene-checklist-heading">
      <h1>{context.title}</h1>
      <button className="btn-secondary scene-checklist-edit-toggle" onClick={() => editing ? finishEditing() : setEditing(true)} disabled={!state.loaded}>
        {editing ? <ListChecks size={16} /> : <Pencil size={16} />}
        {t(editing ? 'sceneChecklist.showChecklist' : 'sceneChecklist.edit')}
      </button>
    </div>
    <span className="scene-checklist-count">{t('sceneChecklist.progress', progress)}</span>
    {state.error && <div className="scene-checklist-error" role="alert">
      <span>{t(state.loaded ? 'sceneChecklist.saveFailed' : 'sceneChecklist.loadFailed')}</span>
      <button className="btn-secondary" onClick={() => state.loaded ? save() : void document.load()}>{t('shell.retry')}</button>
    </div>}
    {!state.loaded ? <p role="status">{t('context.loading')}</p> : <>
      <div className="scene-checklist-scroll">
        <div className="scene-checklist-list">
          {!state.text.trim() && <p className="scene-checklist-hint">{t('sceneChecklist.empty')}</p>}
          <div className="scene-checklist-roots"><ChecklistBlocks blocks={remaining} onCheck={check} disclosure={disclosure} /></div>
          {completed.length > 0 && <details className="scene-checklist-completed" open={expanded.has('completed')} onToggle={(event) => { if (event.target === event.currentTarget) disclosure.toggle('completed', event.currentTarget.open) }}>
            <summary onClick={(event) => { event.preventDefault(); disclosure.toggle('completed', !expanded.has('completed')) }}><CheckCheck size={16} />{t('sceneChecklist.completed', { count: completed.length })}</summary>
            <div className="scene-checklist-roots"><ChecklistBlocks blocks={completed} onCheck={check} disclosure={disclosure} /></div>
          </details>}
        </div>
        {editing && <div className="scene-checklist-editor"><p className="scene-checklist-hint">{t('sceneChecklist.editHint')}</p>
          <MarkdownEditor shared={shared} sharedSession={markdownSession} ariaLabel={t('sceneChecklist.edit')} value={state.text.replace(/\r\n/g, '\n')} onChange={(text) => document.update(preserveChecklistLineEndings(document.state.text, text))} onBlur={() => { if (!shared) document.update(normalizeChecklist(document.state.text)); save() }} minRows={4} />
        </div>}
      </div>
      {!editing && <form className="scene-checklist-add" onSubmit={(event) => { event.preventDefault(); add() }}>
        <input className="dialog-input" aria-label={t('sceneChecklist.add')} placeholder={t('sceneChecklist.add')} value={newItem} onChange={(event) => setNewItem(event.target.value)} />
        <button className="btn-secondary" type="submit" disabled={!newItem.trim()} aria-label={t('sceneChecklist.add')}><Plus size={16} /></button>
      </form>}
    </>}
  </section>
}
