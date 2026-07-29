import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { useProjectStore } from '../../stores/projectStore'
import type { TimelineEventDto } from './TimelineView'

export interface TimelineEventDraft {
  title: string
  date: string
  description: string
  categoryId: string
  linkedChapterGuid: string | null
}

const CATEGORIES = ['plot', 'character', 'location', 'world', 'other']

interface TimelineEventEditorProps {
  initial: TimelineEventDto | null
  onSubmit(draft: TimelineEventDraft): void
  onCancel(): void
  onDelete?(): void
}

export function TimelineEventEditor({
  initial,
  onSubmit,
  onCancel,
  onDelete
}: TimelineEventEditorProps): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const [title, setTitle] = useState(initial?.title ?? '')
  const [date, setDate] = useState(initial?.dateStr ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [categoryId, setCategoryId] = useState(initial?.categoryId ?? 'plot')
  const [linkedChapter, setLinkedChapter] = useState(initial?.chapterGuid ?? '')

  const submit = (): void => {
    if (title.trim().length === 0) return
    onSubmit({
      title: title.trim(),
      date: date.trim(),
      description: description.trim(),
      categoryId,
      linkedChapterGuid: linkedChapter || null
    })
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={t('timeline.eventTitle')}>
        <div className="dialog-title">{t('timeline.eventTitle')}</div>
        <label className="inspector-label" htmlFor="tl-title">
          {t('smartList.name')}
        </label>
        <input
          id="tl-title"
          className="dialog-input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          autoFocus
        />
        <label className="inspector-label" htmlFor="tl-date">
          {t('dialog.dateOptional')}
        </label>
        <input
          id="tl-date"
          className="dialog-input"
          placeholder="1043-03-01"
          value={date}
          onChange={(e) => setDate(e.target.value)}
        />
        <label className="inspector-label" htmlFor="tl-cat">
          {t('timeline.eventCategory')}
        </label>
        <select
          id="tl-cat"
          className="dialog-input"
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
        >
          {CATEGORIES.map((c) => (
            <option key={c} value={c}>
              {t(`timeline.cat${c.charAt(0).toUpperCase() + c.slice(1)}`)}
            </option>
          ))}
        </select>
        <label className="inspector-label" htmlFor="tl-chapter">
          {t('timeline.linkChapter')}
        </label>
        <select
          id="tl-chapter"
          className="dialog-input"
          value={linkedChapter}
          onChange={(e) => setLinkedChapter(e.target.value)}
        >
          <option value="">{t('timeline.noChapter')}</option>
          {chapters.map((c) => (
            <option key={c.guid} value={c.guid}>
              {c.title}
            </option>
          ))}
        </select>
        {/* Not a <label for>: the editor's writing surface is a contenteditable,
            not a form control, so it carries the name via aria-label instead. */}
        <div className="inspector-label">{t('timeline.eventDescription')}</div>
        <MarkdownEditor
          className="md-compact"
          minRows={3}
          ariaLabel={t('timeline.eventDescription')}
          value={description}
          onChange={setDescription}
        />
        <div className="dialog-actions">
          {onDelete && (
            <button className="dialog-button danger" onClick={onDelete}>
              {t('explorer.contextDelete')}
            </button>
          )}
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={submit}>
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
