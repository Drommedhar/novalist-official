import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { useProjectStore } from '../../stores/projectStore'
import type { TimelineEventDto } from './TimelineView'
import { CustomFieldsPanel } from '../../shell/CustomFieldsPanel'

export interface TimelineEventDraft {
  title: string
  date: string
  description: string
  categoryId: string
  linkedChapterGuid: string | null
  /** Who was there. Stored all along and only ever filled in by scene analysis. */
  characters: string[]
  /** Where it happened. Same. */
  locations: string[]
  /** End of the span, or empty for something instantaneous. */
  endDate: string
}

/** Comma-separated names in and out, which is how the chips are stored. */
const split = (value: string): string[] =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0)

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
  const [endDate, setEndDate] = useState(initial?.endDateStr ?? '')
  const [characters, setCharacters] = useState((initial?.characters ?? []).join(', '))
  const [locations, setLocations] = useState((initial?.locations ?? []).join(', '))
  // The timeline prefixes a manual event's id to keep it apart from the
  // generated ones; the stored event knows itself by the bare id.
  const eventId =
    initial?.isManual && initial.id.startsWith('manual-') ? initial.id.slice('manual-'.length) : ''

  const submit = (): void => {
    if (title.trim().length === 0) return
    onSubmit({
      title: title.trim(),
      date: date.trim(),
      description: description.trim(),
      categoryId,
      linkedChapterGuid: linkedChapter || null,
      characters: split(characters),
      locations: split(locations),
      endDate: endDate.trim()
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
        {/* An end turns a marker into a span, which is what makes a war and a
            pregnancy comparable rather than two dots. */}
        <label className="inspector-label" htmlFor="tl-end">
          {t('timeline.endDate')}
        </label>
        <input
          id="tl-end"
          className="dialog-input"
          placeholder={t('timeline.endDatePlaceholder')}
          value={endDate}
          onChange={(e) => setEndDate(e.target.value)}
        />
        {/* Who and where. The model has held both for a long time and only
            scene analysis ever wrote them, so backstory that never appears in
            a scene could not be attached to the people it defines. */}
        <label className="inspector-label" htmlFor="tl-chars">
          {t('timeline.eventCharacters')}
        </label>
        <input
          id="tl-chars"
          className="dialog-input"
          placeholder={t('timeline.namesPlaceholder')}
          value={characters}
          onChange={(e) => setCharacters(e.target.value)}
        />
        <label className="inspector-label" htmlFor="tl-locs">
          {t('timeline.eventLocations')}
        </label>
        <input
          id="tl-locs"
          className="dialog-input"
          placeholder={t('timeline.namesPlaceholder')}
          value={locations}
          onChange={(e) => setLocations(e.target.value)}
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
        {/* Only for an event that exists: a field written against a draft
            would have nothing to be stored on. */}
        {eventId && <CustomFieldsPanel scope="Event" id={eventId} />}
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
