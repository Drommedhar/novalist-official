import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface Submission {
  id: string
  recipient: string
  material: string
  sentOn: string
  status: string
  respondedOn: string
  notes: string
  /** True while this one is still out. */
  isOpen: boolean
}

const STATUSES = ['sent', 'requested', 'accepted', 'rejected', 'withdrawn', 'noReply']

/**
 * Where this book has been sent and what came back.
 *
 * Novalist produced submission-ready material — the Exposé, the Shunn layout —
 * and recorded nothing about where any of it went. So the one thing a writer
 * must not do, send the same manuscript to the same agent twice, was the one
 * thing the app could not help with.
 */
export function SubmissionsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [items, setItems] = useState<Submission[]>([])
  const [recipient, setRecipient] = useState('')
  const [alreadyOut, setAlreadyOut] = useState<string[]>([])

  const load = useCallback(() => {
    void rpc
      .request<Submission[]>('submissions/list')
      .then(setItems)
      .catch(() => setItems([]))
  }, [])

  useEffect(load, [load])

  // Checked as the name is typed, so the warning arrives before the send
  // rather than after it.
  useEffect(() => {
    const name = recipient.trim()
    if (name.length === 0) {
      setAlreadyOut([])
      return
    }
    void rpc
      .request<string[]>('submissions/openWith', [name])
      .then(setAlreadyOut)
      .catch(() => setAlreadyOut([]))
  }, [recipient])

  const add = (): void => {
    const name = recipient.trim()
    if (name.length === 0) return
    void rpc.request<Submission[]>('submissions/save', [null, name]).then((next) => {
      setItems(next)
      setRecipient('')
    })
  }

  const update = (item: Submission, patch: Partial<Submission>): void => {
    const merged = { ...item, ...patch }
    void rpc
      .request<Submission[]>('submissions/save', [
        merged.id,
        merged.recipient,
        merged.material,
        merged.sentOn,
        merged.status,
        merged.respondedOn,
        merged.notes
      ])
      .then(setItems)
  }

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('submissions.title')}</div>
      <div className="dashboard-echo-desc">{t('submissions.intro')}</div>

      <div className="submission-add">
        <input
          className="inspector-input"
          placeholder={t('submissions.recipientPlaceholder')}
          value={recipient}
          onChange={(e) => setRecipient(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        <button className="btn-secondary" disabled={recipient.trim().length === 0} onClick={add}>
          <Plus size={13} strokeWidth={2} /> {t('submissions.add')}
        </button>
      </div>

      {/* Reported, not refused. Querying the same agency twice on purpose —
          a different agent there, a re-query after a rewrite — is normal, and
          an app that blocks it is one the writer works around. */}
      {alreadyOut.length > 0 && (
        <p className="submission-warning">
          {t('submissions.alreadyOut', {
            count: alreadyOut.length,
            when: alreadyOut.filter((w) => w.length > 0).join(', ')
          })}
        </p>
      )}

      {items.length === 0 ? (
        <p className="settings-hint">{t('submissions.empty')}</p>
      ) : (
        items.map((item) => (
          <div key={item.id} className={`submission${item.isOpen ? ' out' : ''}`}>
            <div className="submission-row">
              <input
                className="submission-name"
                defaultValue={item.recipient}
                aria-label={t('submissions.recipient')}
                onBlur={(e) =>
                  e.target.value !== item.recipient && update(item, { recipient: e.target.value })
                }
              />
              <select
                className="inspector-input submission-status"
                aria-label={t('submissions.status')}
                value={item.status}
                onChange={(e) => update(item, { status: e.target.value })}
              >
                {STATUSES.map((s) => (
                  <option key={s} value={s}>
                    {t(`submissions.status_${s}`)}
                  </option>
                ))}
              </select>
              <button
                className="binder-row-action"
                aria-label={t('submissions.remove')}
                title={t('submissions.remove')}
                onClick={() =>
                  void rpc.request<Submission[]>('submissions/remove', [item.id]).then(setItems)
                }
              >
                <Trash2 size={12} strokeWidth={2} />
              </button>
            </div>
            <div className="submission-row">
              {/* Free text rather than a date picker: a half-remembered
                  "March" is worth recording, and a picker demands a day. */}
              <input
                className="inspector-input"
                placeholder={t('submissions.sentOn')}
                defaultValue={item.sentOn}
                onBlur={(e) =>
                  e.target.value !== item.sentOn && update(item, { sentOn: e.target.value })
                }
              />
              <input
                className="inspector-input"
                placeholder={t('submissions.material')}
                defaultValue={item.material}
                onBlur={(e) =>
                  e.target.value !== item.material && update(item, { material: e.target.value })
                }
              />
              <input
                className="inspector-input"
                placeholder={t('submissions.respondedOn')}
                defaultValue={item.respondedOn}
                onBlur={(e) =>
                  e.target.value !== item.respondedOn &&
                  update(item, { respondedOn: e.target.value })
                }
              />
            </div>
            <input
              className="links-note"
              placeholder={t('submissions.notesPlaceholder')}
              defaultValue={item.notes}
              onBlur={(e) =>
                e.target.value !== item.notes && update(item, { notes: e.target.value })
              }
            />
          </div>
        ))
      )}
    </div>
  )
}
