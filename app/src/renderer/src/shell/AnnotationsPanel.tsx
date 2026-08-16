import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { useShellStore } from '../stores/shellStore'

interface CommentRow {
  id: string
  anchorText: string
  text: string
  resolved: boolean
}

interface FootnoteRow {
  id: string
  number: number
  text: string
}

/** Standalone footnotes + comments lists for the open scene, editable and
 * deletable, persisted through scenes/setAnnotations. */
export function AnnotationsPanel({
  chapterGuid,
  sceneId
}: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [comments, setComments] = useState<CommentRow[]>([])
  const [footnotes, setFootnotes] = useState<FootnoteRow[]>([])
  // Any write to the scene's annotations, from here or from the prose. This
  // list showed whatever it had read when the scene opened, so a footnote made
  // in the editor did not appear until the writer left the scene and came back.
  const annotationsRevision = useEditorBridge((s) => s.annotationsRevision)
  const pendingFootnoteText = useShellStore((s) => s.pendingFootnoteText)
  const textBoxes = useRef(new Map<string, HTMLInputElement>())

  const load = (): void => {
    void rpc
      .request<{ comments: CommentRow[]; footnotes: FootnoteRow[] }>('scenes/getAnnotations', [
        chapterGuid,
        sceneId
      ])
      .then((a) => {
        setComments(a.comments)
        setFootnotes(a.footnotes)
      })
      .catch(() => {
        setComments([])
        setFootnotes([])
      })
  }

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chapterGuid, sceneId, annotationsRevision])

  /**
   * The caret goes into the note that was just made, so it can be written now
   * rather than found later. The row has to exist first, which is why this
   * waits on the reloaded list rather than running where the request is made.
   */
  useEffect(() => {
    if (!pendingFootnoteText) return
    const box = textBoxes.current.get(pendingFootnoteText)
    if (!box) return
    useShellStore.getState().clearPendingFootnoteText()
    box.focus()
  }, [pendingFootnoteText, footnotes])

  const persist = (
    nextComments: CommentRow[],
    nextFootnotes: FootnoteRow[]
  ): Promise<void> =>
    rpc
      .request('scenes/setAnnotations', [chapterGuid, sceneId, nextComments, nextFootnotes])
      .then(() => useEditorBridge.getState().annotationsChanged())
      .catch(() => undefined)

  /**
   * The editor, but only while it is showing the scene this panel is about.
   *
   * The panel used to write the shortened list to the backend and tell the
   * prose nothing, so a deleted footnote kept its number in the text and a
   * deleted comment kept its highlight - and the next save wrote the marker
   * straight back out of the editor's own HTML.
   */
  const proseEditor = (): ReturnType<typeof useEditorBridge.getState>['editor'] => {
    const bridge = useEditorBridge.getState()
    return bridge.isShowing(sceneId) ? bridge.editor : null
  }

  /** Takes the writer to the marker, which is the only way to judge a note. */
  const showFootnote = (id: string): void => proseEditor()?.scrollToFootnoteById(id)

  const removeFootnote = (index: number): void => {
    const gone = footnotes[index]
    const next = footnotes.filter((_, i) => i !== index)
    setFootnotes(next)
    // The prose is told after the shortened list is saved: taking a marker out
    // renumbers the rest, and the editor answers that by reading the list back.
    // The other order has it read the list this row was still in.
    void persist(comments, next).then(() => proseEditor()?.removeFootnoteById(gone.id))
  }

  const removeComment = (index: number): void => {
    const gone = comments[index]
    const next = comments.filter((_, i) => i !== index)
    setComments(next)
    void persist(next, footnotes).then(() => proseEditor()?.removeCommentById(gone.id))
  }

  // A blank panel says nothing about whether it is working. The tab is where
  // footnotes live whether or not the scene has any, so it says so.
  if (comments.length === 0 && footnotes.length === 0) {
    return <div className="inspector-placeholder">{t('footnotes.empty')}</div>
  }

  return (
    <>
      {footnotes.length > 0 && (
        <div className="ctx-section">
          <div className="inspector-label">{t('footnotes.panelTitle')}</div>
          {footnotes.map((fn, index) => (
            <div key={fn.id} className="annotation-row">
              {/* The number is where it sits in the prose, so it is also how
                  to get there. A note you cannot find in the text is one you
                  cannot judge. */}
              <button
                className="annotation-num"
                title={t('footnotes.showInText')}
                aria-label={t('footnotes.showInText')}
                onClick={() => showFootnote(fn.id)}
              >
                {fn.number}
              </button>
              <input
                className="outliner-input"
                ref={(el) => {
                  if (el) textBoxes.current.set(fn.id, el)
                  else textBoxes.current.delete(fn.id)
                }}
                placeholder={t('footnotes.addPrompt')}
                defaultValue={fn.text}
                onBlur={(e) => {
                  if (e.target.value === fn.text) return
                  const next = footnotes.map((f, i) =>
                    i === index ? { ...f, text: e.target.value } : f
                  )
                  setFootnotes(next)
                  void persist(comments, next)
                }}
              />
              <button
                className="binder-expand"
                aria-label={t('explorer.contextDelete')}
                onClick={() => removeFootnote(index)}
              >
                <X size={12} strokeWidth={2} />
              </button>
            </div>
          ))}
        </div>
      )}

      {comments.length > 0 && (
        <div className="ctx-section">
          <div className="inspector-label">{t('comments.panelTitle')}</div>
          {comments.map((comment, index) => (
            <div
              key={comment.id}
              className={`annotation-comment${comment.resolved ? ' resolved' : ''}`}
            >
              {comment.anchorText && (
                <span className="annotation-anchor" title={comment.anchorText}>
                  {comment.anchorText}
                </span>
              )}
              <div className="annotation-row">
                <input
                  className="outliner-input"
                  defaultValue={comment.text}
                  onBlur={(e) => {
                    if (e.target.value === comment.text) return
                    const next = comments.map((c, i) =>
                      i === index ? { ...c, text: e.target.value } : c
                    )
                    setComments(next)
                    void persist(next, footnotes)
                  }}
                />
                <button
                  className="binder-expand"
                  aria-label={t('explorer.contextDelete')}
                  onClick={() => removeComment(index)}
                >
                  <X size={12} strokeWidth={2} />
                </button>
              </div>
              <label className="annotation-resolve">
                <input
                  type="checkbox"
                  checked={comment.resolved}
                  onChange={(e) => {
                    const next = comments.map((c, i) =>
                      i === index ? { ...c, resolved: e.target.checked } : c
                    )
                    setComments(next)
                    void persist(next, footnotes)
                  }}
                />
                {t('comments.resolved')}
              </label>
            </div>
          ))}
        </div>
      )}
    </>
  )
}
