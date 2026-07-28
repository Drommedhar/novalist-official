import { useCallback, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Bold, Heading, Italic, Link2, List, ListOrdered, Quote, Strikethrough } from 'lucide-react'
import { EditorState, RangeSetBuilder, type ChangeSpec, type Range } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  ViewPlugin,
  WidgetType,
  keymap,
  placeholder as cmPlaceholder,
  type DecorationSet,
  type ViewUpdate
} from '@codemirror/view'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown, markdownLanguage } from '@codemirror/lang-markdown'
import { HighlightStyle, syntaxHighlighting, syntaxTree } from '@codemirror/language'
import { tags } from '@lezer/highlight'
import './markdownEditor.css'

/**
 * The markdown fields in this app (Codex sections and overrides, research
 * notes, event descriptions, entity long text) are stored and exported as
 * plain Markdown, and the Wiki renders them. Writing them used to mean typing
 * the syntax into a bare textarea with nothing to show for it until you opened
 * the Wiki.
 *
 * This is a live-styled editor rather than a WYSIWYG one: the document *is* the
 * Markdown text, and CodeMirror styles it in place - headings set larger, bold
 * actually bold, list markers in the accent. Nothing is round-tripped through
 * HTML, so what reaches disk is exactly what the user typed and existing files
 * cannot be reformatted behind their back. The toolbar and shortcuts are there
 * so a writer who does not know the syntax never has to learn it.
 */

/** Maps Markdown syntax to classes; the actual sizes/colours live in the CSS so
 *  they can use the design tokens rather than hardcoded values. */
const markdownHighlight = HighlightStyle.define([
  { tag: tags.heading1, class: 'cm-md-h1' },
  { tag: tags.heading2, class: 'cm-md-h2' },
  { tag: tags.heading3, class: 'cm-md-h3' },
  { tag: [tags.heading4, tags.heading5, tags.heading6], class: 'cm-md-h4' },
  { tag: tags.strong, class: 'cm-md-strong' },
  { tag: tags.emphasis, class: 'cm-md-em' },
  { tag: tags.strikethrough, class: 'cm-md-strike' },
  { tag: tags.link, class: 'cm-md-link' },
  { tag: tags.url, class: 'cm-md-url' },
  { tag: tags.monospace, class: 'cm-md-code' },
  { tag: tags.quote, class: 'cm-md-quote' },
  { tag: tags.list, class: 'cm-md-list' },
  // The punctuation that makes it Markdown - the #, the **, the backticks.
  // Hidden by the plugin below except on the line being edited; this dims
  // whatever remains visible.
  { tag: tags.processingInstruction, class: 'cm-md-mark' }
])

/* ===== Concealing the syntax =====
 * A writer who does not know Markdown should not have to look at it. These
 * marks are hidden on every line except the one the caret is on, so the field
 * reads as formatted prose but the moment you edit a line its syntax comes
 * back and can be changed by hand. The document itself is untouched - this is
 * purely how it is drawn. */
const CONCEALED = new Set([
  'HeaderMark',
  'EmphasisMark',
  'StrongEmphasisMark',
  'StrikethroughMark',
  'CodeMark',
  'LinkMark',
  'QuoteMark',
  'URL'
])

/** Bullet lists keep a marker - hiding it entirely would leave the item's text
 *  floating with no sign it belongs to a list. */
class BulletWidget extends WidgetType {
  eq(): boolean {
    return true
  }
  toDOM(): HTMLElement {
    const dot = document.createElement('span')
    dot.className = 'cm-md-bullet'
    dot.textContent = '•'
    return dot
  }
}

const HIDE = Decoration.replace({})
const BULLET = Decoration.replace({ widget: new BulletWidget() })

function concealDecorations(view: EditorView): DecorationSet {
  // Lines the caret or selection touches keep their syntax visible - but only
  // while the field actually has focus. The selection outlives the focus, so
  // without this check a field the user had clicked once would keep showing the
  // syntax on that line forever after.
  const active = new Set<number>()
  if (view.hasFocus) {
    for (const range of view.state.selection.ranges) {
      const first = view.state.doc.lineAt(range.from).number
      const last = view.state.doc.lineAt(range.to).number
      for (let n = first; n <= last; n += 1) active.add(n)
    }
  }

  const found: Range<Decoration>[] = []
  for (const { from, to } of view.visibleRanges) {
    syntaxTree(view.state).iterate({
      from,
      to,
      enter: (node) => {
        if (node.from === node.to) return
        if (active.has(view.state.doc.lineAt(node.from).number)) return
        if (node.name === 'ListMark') {
          // Only unordered markers become a bullet; "1." is already readable.
          const text = view.state.sliceDoc(node.from, node.to)
          if (/^[-*+]$/.test(text)) found.push(BULLET.range(node.from, node.to))
          return
        }
        if (CONCEALED.has(node.name)) found.push(HIDE.range(node.from, node.to))
      }
    })
  }

  // RangeSetBuilder demands sorted input and the tree walk can nest, so sort
  // rather than trusting visit order.
  found.sort((a, b) => a.from - b.from || a.to - b.to)
  const builder = new RangeSetBuilder<Decoration>()
  for (const range of found) builder.add(range.from, range.to, range.value)
  return builder.finish()
}

const concealSyntax = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet
    constructor(view: EditorView) {
      this.decorations = concealDecorations(view)
    }
    update(update: ViewUpdate): void {
      // Selection moves reveal a different line; focus changes reveal or
      // conceal the whole field.
      if (
        update.docChanged ||
        update.selectionSet ||
        update.viewportChanged ||
        update.focusChanged
      ) {
        this.decorations = concealDecorations(update.view)
      }
    }
  },
  {
    decorations: (plugin) => plugin.decorations,
    // Makes the caret step over a concealed run in one press instead of
    // stopping inside characters that are not on screen.
    provide: (plugin) =>
      EditorView.atomicRanges.of((view) => view.plugin(plugin)?.decorations ?? Decoration.none)
  }
)

export type MarkdownAction =
  | 'bold'
  | 'italic'
  | 'strikethrough'
  | 'heading'
  | 'bullet'
  | 'ordered'
  | 'quote'
  | 'link'

/** Wraps the selection in `mark`, or unwraps it when it is already wrapped. */
function toggleWrap(view: EditorView, mark: string): void {
  const changes: ChangeSpec[] = []
  const { state } = view
  for (const range of state.selection.ranges) {
    const before = state.sliceDoc(Math.max(0, range.from - mark.length), range.from)
    const after = state.sliceDoc(range.to, Math.min(state.doc.length, range.to + mark.length))
    if (before === mark && after === mark) {
      changes.push({ from: range.from - mark.length, to: range.from, insert: '' })
      changes.push({ from: range.to, to: range.to + mark.length, insert: '' })
    } else {
      changes.push({ from: range.from, insert: mark })
      changes.push({ from: range.to, insert: mark })
    }
  }
  view.dispatch({ changes, scrollIntoView: true })
  view.focus()
}

/** Adds or removes a line prefix (#, -, >, 1.) on every line the selection touches. */
function toggleLinePrefix(view: EditorView, prefix: string, ordered = false): void {
  const { state } = view
  const changes: ChangeSpec[] = []
  const seen = new Set<number>()
  for (const range of state.selection.ranges) {
    let line = state.doc.lineAt(range.from)
    const last = state.doc.lineAt(range.to)
    let n = 1
    for (;;) {
      if (!seen.has(line.number)) {
        seen.add(line.number)
        const existing = ordered ? /^\d+\.\s/ : new RegExp(`^${prefix.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`)
        const match = line.text.match(existing)
        if (match) changes.push({ from: line.from, to: line.from + match[0].length, insert: '' })
        else changes.push({ from: line.from, insert: ordered ? `${n}. ` : prefix })
        n += 1
      }
      if (line.number >= last.number) break
      line = state.doc.line(line.number + 1)
    }
  }
  view.dispatch({ changes, scrollIntoView: true })
  view.focus()
}

function insertLink(view: EditorView): void {
  const { state } = view
  const changes: ChangeSpec[] = []
  for (const range of state.selection.ranges) {
    const text = state.sliceDoc(range.from, range.to)
    changes.push({ from: range.from, to: range.to, insert: `[${text}](url)` })
  }
  view.dispatch({ changes, scrollIntoView: true })
  view.focus()
}

export function applyMarkdownAction(view: EditorView, action: MarkdownAction): void {
  switch (action) {
    case 'bold':
      return toggleWrap(view, '**')
    case 'italic':
      return toggleWrap(view, '*')
    case 'strikethrough':
      return toggleWrap(view, '~~')
    case 'heading':
      return toggleLinePrefix(view, '## ')
    case 'bullet':
      return toggleLinePrefix(view, '- ')
    case 'ordered':
      return toggleLinePrefix(view, '', true)
    case 'quote':
      return toggleLinePrefix(view, '> ')
    case 'link':
      return insertLink(view)
  }
}

export function MarkdownEditor({
  value,
  onChange,
  onBlur,
  minRows = 3,
  placeholder,
  ariaLabel,
  className = ''
}: {
  value: string
  onChange: (next: string) => void
  /** Fired when focus leaves the editor, matching the textarea it replaces -
   *  every call site persists on blur. */
  onBlur?: () => void
  minRows?: number
  placeholder?: string
  ariaLabel?: string
  className?: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const host = useRef<HTMLDivElement | null>(null)
  const view = useRef<EditorView | null>(null)
  // Kept in refs so the CodeMirror extensions can stay static: rebuilding the
  // view on every keystroke would lose the selection and the undo history.
  const onChangeRef = useRef(onChange)
  onChangeRef.current = onChange
  const onBlurRef = useRef(onBlur)
  onBlurRef.current = onBlur

  useEffect(() => {
    if (!host.current) return
    const state = EditorState.create({
      doc: value,
      extensions: [
        history(),
        keymap.of([
          {
            key: 'Mod-b',
            run: (v) => (applyMarkdownAction(v, 'bold'), true)
          },
          {
            key: 'Mod-i',
            run: (v) => (applyMarkdownAction(v, 'italic'), true)
          },
          ...historyKeymap,
          // Without Enter/Backspace etc. the field would not accept newlines.
          ...defaultKeymap.filter((b) => b.key !== 'Mod-i' && b.key !== 'Mod-b')
        ]),
        markdown({ base: markdownLanguage }),
        syntaxHighlighting(markdownHighlight),
        concealSyntax,
        EditorView.lineWrapping,
        ...(placeholder ? [cmPlaceholder(placeholder)] : []),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) onChangeRef.current(update.state.doc.toString())
          if (update.focusChanged && !update.view.hasFocus) onBlurRef.current?.()
        }),
        EditorView.contentAttributes.of(ariaLabel ? { 'aria-label': ariaLabel } : {})
      ]
    })
    const created = new EditorView({ state, parent: host.current })
    view.current = created
    return () => {
      created.destroy()
      view.current = null
    }
    // Built once: `value` is synced by the effect below, not by re-creating it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Adopt an externally changed value (switching entity, discarding an
  // override), but never while the user is mid-edit in this field.
  useEffect(() => {
    const v = view.current
    if (!v || v.hasFocus) return
    const current = v.state.doc.toString()
    if (current === value) return
    v.dispatch({ changes: { from: 0, to: current.length, insert: value } })
  }, [value])

  const act = useCallback((action: MarkdownAction) => {
    if (view.current) applyMarkdownAction(view.current, action)
  }, [])

  const buttons: { action: MarkdownAction; icon: React.ReactNode; labelKey: string }[] = [
    { action: 'bold', icon: <Bold size={14} strokeWidth={2} />, labelKey: 'markdown.bold' },
    { action: 'italic', icon: <Italic size={14} strokeWidth={2} />, labelKey: 'markdown.italic' },
    {
      action: 'strikethrough',
      icon: <Strikethrough size={14} strokeWidth={2} />,
      labelKey: 'markdown.strikethrough'
    },
    { action: 'heading', icon: <Heading size={14} strokeWidth={2} />, labelKey: 'markdown.heading' },
    { action: 'bullet', icon: <List size={14} strokeWidth={2} />, labelKey: 'markdown.bulletList' },
    {
      action: 'ordered',
      icon: <ListOrdered size={14} strokeWidth={2} />,
      labelKey: 'markdown.orderedList'
    },
    { action: 'quote', icon: <Quote size={14} strokeWidth={2} />, labelKey: 'markdown.quote' },
    { action: 'link', icon: <Link2 size={14} strokeWidth={2} />, labelKey: 'markdown.link' }
  ]

  return (
    <div className={`md-editor ${className}`.trim()}>
      <div className="md-toolbar" role="toolbar" aria-label={t('markdown.toolbar')}>
        {buttons.map((b) => (
          <button
            key={b.action}
            type="button"
            className="md-toolbar-btn"
            title={t(b.labelKey)}
            aria-label={t(b.labelKey)}
            // The editor keeps focus and the selection, so the action applies
            // to what the user had highlighted.
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => act(b.action)}
          >
            {b.icon}
          </button>
        ))}
      </div>
      <div
        className="md-surface"
        style={{ minHeight: `calc(${minRows} * var(--nl-leading-body) * 1em)` }}
        ref={host}
      />
    </div>
  )
}
