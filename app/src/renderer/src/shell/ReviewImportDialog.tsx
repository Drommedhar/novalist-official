import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileInput, MessageSquarePlus } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'

interface ReviewComment {
  id: string
  author: string
  date: string
  text: string
  anchorText: string
}

interface ReviewRevision {
  kind: string
  author: string
  date: string
  text: string
}

interface Review {
  comments: ReviewComment[]
  revisions: ReviewRevision[]
}

/**
 * Reads an editor's marked-up Word file and shows what they changed.
 *
 * Tracked changes are shown, not applied: accepting them would rewrite prose
 * from a file Novalist did not produce the layout for, and a wrong automatic
 * edit to the manuscript is worse than a list the writer works through.
 * Comments can be attached to the open scene, because that is additive.
 */
export function ReviewImportDialog(props: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)

  const [review, setReview] = useState<Review | null>(null)
  const [fileName, setFileName] = useState('')
  const [busy, setBusy] = useState(false)
  const [applied, setApplied] = useState<number | null>(null)

  const pick = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('review.choose'), 'all')
    if (!path) return

    setBusy(true)
    setApplied(null)
    try {
      setFileName(path.split(/[\\/]/).pop() ?? path)
      setReview(await rpc.request<Review>('review/read', [path]))
    } finally {
      setBusy(false)
    }
  }

  const applyComments = async (): Promise<void> => {
    if (!review || !openChapterGuid || !openSceneId) return
    setBusy(true)
    try {
      setApplied(
        await rpc.request<number>('review/applyComments', [
          openChapterGuid,
          openSceneId,
          review.comments
        ])
      )
    } finally {
      setBusy(false)
    }
  }

  const canApply = Boolean(review?.comments.length && openSceneId)

  return (
    <div className="dialog-backdrop" onClick={props.onClose}>
      <div className="dialog review-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="dialog-header">
          <h3>{t('review.title')}</h3>
          <button className="dialog-close" onClick={props.onClose} aria-label={t('dialog.close')}>
            ×
          </button>
        </div>

        <p className="settings-hint">{t('review.description')}</p>

        <div className="settings-button-row">
          <button className="dialog-button" disabled={busy} onClick={() => void pick()}>
            <FileInput size={14} /> {t('review.choose')}
          </button>
          {fileName && <span className="settings-hint">{fileName}</span>}
        </div>

        {review && review.comments.length === 0 && review.revisions.length === 0 && (
          <p className="settings-hint">{t('review.nothingFound')}</p>
        )}

        {review && review.comments.length > 0 && (
          <>
            <h4>{t('review.comments', { count: review.comments.length })}</h4>
            <ul className="review-list">
              {review.comments.map((c) => (
                <li key={c.id}>
                  <div className="review-meta">{c.author || t('review.unknownAuthor')}</div>
                  <div className="review-text">{c.text}</div>
                  {c.anchorText && <div className="review-anchor">{c.anchorText}</div>}
                </li>
              ))}
            </ul>
            <div className="settings-button-row">
              <button className="dialog-button" disabled={!canApply || busy} onClick={() => void applyComments()}>
                <MessageSquarePlus size={14} /> {t('review.applyComments')}
              </button>
              {!openSceneId && <span className="settings-hint">{t('review.needsOpenScene')}</span>}
              {applied !== null && (
                <span className="settings-hint">{t('review.applied', { count: applied })}</span>
              )}
            </div>
          </>
        )}

        {review && review.revisions.length > 0 && (
          <>
            <h4>{t('review.revisions', { count: review.revisions.length })}</h4>
            <p className="settings-hint">{t('review.revisionsHint')}</p>
            <ul className="review-list">
              {review.revisions.map((r, i) => (
                <li key={`${r.kind}-${i}`}>
                  <div className="review-meta">
                    {t(`review.kind.${r.kind}`)} · {r.author || t('review.unknownAuthor')}
                  </div>
                  <div className={r.kind === 'delete' ? 'review-text review-deleted' : 'review-text'}>
                    {r.text}
                  </div>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </div>
  )
}
