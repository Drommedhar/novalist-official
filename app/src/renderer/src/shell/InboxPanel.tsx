import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, CornerDownRight, ListTodo, RotateCcw } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'

interface InboxReply {
  id: string
  author: string
  text: string
  createdAt: string
}

interface InboxItem {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  commentId: string
  anchorText: string
  text: string
  author: string
  isTodo: boolean
  resolved: boolean
  createdAt: string
  replies: InboxReply[]
}

/**
 * Every open note in the book, in one place.
 *
 * A comment could only be found by reopening the scene it was left in - there
 * was no query across them at all - so a note to yourself was lost the moment
 * you closed the scene, and an editor's question had nowhere to be answered.
 */
export function InboxPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [items, setItems] = useState<InboxItem[]>([])
  const [showResolved, setShowResolved] = useState(false)
  const [todosOnly, setTodosOnly] = useState(false)
  const [replyTo, setReplyTo] = useState<string | null>(null)
  const [replyText, setReplyText] = useState('')

  const load = (resolved: boolean): void => {
    void rpc
      .request<InboxItem[]>('inbox/list', [resolved])
      .then(setItems)
      .catch(() => setItems([]))
  }

  useEffect(() => load(showResolved), [showResolved])

  const shown = todosOnly ? items.filter((i) => i.isTodo) : items

  return (
    <div className="inbox">
      <div className="inbox-filters">
        <label className="match-toggle">
          <input
            type="checkbox"
            checked={todosOnly}
            onChange={(e) => setTodosOnly(e.target.checked)}
          />
          {t('inbox.todosOnly')}
        </label>
        <label className="match-toggle">
          <input
            type="checkbox"
            checked={showResolved}
            onChange={(e) => setShowResolved(e.target.checked)}
          />
          {t('inbox.showResolved')}
        </label>
      </div>

      {shown.length === 0 && <div className="inbox-empty">{t('inbox.empty')}</div>}

      {shown.map((item) => (
        <div key={item.commentId} className={`inbox-item${item.resolved ? ' resolved' : ''}`}>
          <button
            className="inbox-where"
            onClick={() =>
              void useProjectStore.getState().openScene(item.chapterGuid, item.sceneId)
            }
          >
            {item.chapterTitle} - {item.sceneTitle}
          </button>
          {/* What the note was attached to, so it reads as being about
              something rather than floating free of the prose. */}
          {item.anchorText && <div className="inbox-anchor">{item.anchorText}</div>}
          <div className="inbox-text">{item.text}</div>
          {item.author && <div className="inbox-author">{item.author}</div>}

          {item.replies.map((reply) => (
            <div key={reply.id} className="inbox-reply">
              <CornerDownRight size={11} strokeWidth={2} />
              <span className="inbox-reply-text">{reply.text}</span>
              {reply.author && <span className="inbox-author">{reply.author}</span>}
            </div>
          ))}

          <div className="inbox-actions">
            <button
              className="inbox-action"
              title={t(item.resolved ? 'inbox.reopen' : 'inbox.resolve')}
              onClick={() =>
                void rpc
                  .request<InboxItem[]>('inbox/setResolved', [
                    item.sceneId,
                    item.commentId,
                    !item.resolved
                  ])
                  .then(() => load(showResolved))
              }
            >
              {item.resolved ? <RotateCcw size={12} /> : <Check size={12} />}
            </button>
            <button
              className={`inbox-action${item.isTodo ? ' active' : ''}`}
              title={t(item.isTodo ? 'inbox.notATodo' : 'inbox.makeTodo')}
              onClick={() =>
                void rpc
                  .request<InboxItem[]>('inbox/setTodo', [
                    item.sceneId,
                    item.commentId,
                    !item.isTodo
                  ])
                  .then(() => load(showResolved))
              }
            >
              <ListTodo size={12} />
            </button>
            <button
              className="inbox-action"
              title={t('inbox.reply')}
              onClick={() => {
                setReplyTo(replyTo === item.commentId ? null : item.commentId)
                setReplyText('')
              }}
            >
              <CornerDownRight size={12} />
            </button>
          </div>

          {replyTo === item.commentId && (
            <input
              className="inspector-input"
              autoFocus
              placeholder={t('inbox.replyPlaceholder')}
              value={replyText}
              onChange={(e) => setReplyText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key !== 'Enter' || replyText.trim().length === 0) return
                void rpc
                  .request('inbox/reply', [item.sceneId, item.commentId, replyText.trim()])
                  .then(() => {
                    setReplyTo(null)
                    setReplyText('')
                    load(showResolved)
                  })
              }}
            />
          )}
        </div>
      ))}
    </div>
  )
}
