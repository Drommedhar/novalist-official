import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { rpc } from '../rpc/client'

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
  }, [chapterGuid, sceneId])

  const persist = (nextComments: CommentRow[], nextFootnotes: FootnoteRow[]): void => {
    void rpc.request('scenes/setAnnotations', [chapterGuid, sceneId, nextComments, nextFootnotes])
  }

  if (comments.length === 0 && footnotes.length === 0) return null

  return (
    <>
      {footnotes.length > 0 && (
        <div className="ctx-section">
          <div className="inspector-label">{t('footnotes.panelTitle')}</div>
          {footnotes.map((fn, index) => (
            <div key={fn.id} className="annotation-row">
              <span className="annotation-num">{fn.number}</span>
              <input
                className="outliner-input"
                defaultValue={fn.text}
                onBlur={(e) => {
                  if (e.target.value === fn.text) return
                  const next = footnotes.map((f, i) =>
                    i === index ? { ...f, text: e.target.value } : f
                  )
                  setFootnotes(next)
                  persist(comments, next)
                }}
              />
              <button
                className="binder-expand"
                aria-label={t('explorer.contextDelete')}
                onClick={() => {
                  const next = footnotes.filter((_, i) => i !== index)
                  setFootnotes(next)
                  persist(comments, next)
                }}
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
                    persist(next, footnotes)
                  }}
                />
                <button
                  className="binder-expand"
                  aria-label={t('explorer.contextDelete')}
                  onClick={() => {
                    const next = comments.filter((_, i) => i !== index)
                    setComments(next)
                    persist(next, footnotes)
                  }}
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
                    persist(next, footnotes)
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
