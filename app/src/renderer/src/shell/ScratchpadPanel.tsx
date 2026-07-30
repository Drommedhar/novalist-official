import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { rpc } from '../rpc/client'

interface ScratchpadNote {
  id: string
  text: string
  createdAt: string
}

/**
 * Loose notes that belong to the writer rather than to a project.
 *
 * Quick Capture files into the open project's research inbox, which means a
 * thought arriving before the right project is open has nowhere to go - and the
 * moment a thought arrives is exactly the moment somebody is not sitting in
 * front of the project it belongs to. These live beside the settings, survive
 * every project being closed, and are filed into whichever one opens later.
 *
 * Deliberately a flat list: a scratchpad that needs organising is a second
 * research library, and the point of this one is that using it costs nothing.
 */
export function ScratchpadPanel({
  canFile,
  onFiled
}: {
  /** Whether a project is open to file a note into. */
  canFile: boolean
  onFiled?(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [notes, setNotes] = useState<ScratchpadNote[]>([])
  const [text, setText] = useState('')

  useEffect(() => {
    void rpc
      .request<ScratchpadNote[]>('scratchpad/list')
      .then(setNotes)
      .catch(() => setNotes([]))
  }, [])

  const add = async (): Promise<void> => {
    if (text.trim().length === 0) return
    setNotes(await rpc.request<ScratchpadNote[]>('scratchpad/add', [text]))
    setText('')
  }

  return (
    <div className="start-recents">
      <div className="start-recents-label">{t('scratchpad.title')}</div>
      <p className="scratchpad-empty">{t('scratchpad.intro')}</p>

      <textarea
        className="dialog-input quick-capture-input"
        rows={3}
        value={text}
        placeholder={t('scratchpad.placeholder')}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          // Enter alone inserts a newline: a jotted thought is often more
          // than one line, and losing the second half is worse than a shortcut.
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault()
            void add()
          }
        }}
      />
      <div className="dialog-actions">
        <button
          className="dialog-button primary"
          disabled={text.trim().length === 0}
          onClick={() => void add()}
        >
          {t('scratchpad.add')}
        </button>
      </div>

      <div className="scratchpad-list">
        {notes.map((note) => (
          <div key={note.id} className="scratchpad-note">
            <div className="scratchpad-note-text">
              {note.text}
              <div className="scratchpad-note-date">
                {new Date(note.createdAt).toLocaleDateString()}
              </div>
            </div>
            {/* Only offered with a project open, because otherwise there is
                nowhere to put it and the button would do nothing. */}
            {canFile && (
              <button
                className="dialog-button"
                onClick={() =>
                  void rpc
                    .request<ScratchpadNote[]>('scratchpad/fileIntoProject', [note.id])
                    .then((next) => {
                      setNotes(next)
                      onFiled?.()
                    })
                }
              >
                {t('scratchpad.file')}
              </button>
            )}
            <button
              className="research-star"
              aria-label={t('scratchpad.delete')}
              onClick={() =>
                void rpc
                  .request<ScratchpadNote[]>('scratchpad/delete', [note.id])
                  .then(setNotes)
              }
            >
              <Trash2 size={14} strokeWidth={2} />
            </button>
          </div>
        ))}
        {notes.length === 0 && <div className="scratchpad-empty">{t('scratchpad.empty')}</div>}
      </div>
    </div>
  )
}
