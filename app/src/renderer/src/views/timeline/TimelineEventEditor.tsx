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
  /** Which timelines it sits on. Empty means the first one. */
  timelineIds: string[]
  /** The event this one hangs off, or empty for a date of its own. */
  dependsOnEventId: string
  /** Days after the anchor. Negative puts it before. */
  dependsOnOffsetDays: number
  /** 'start' or 'end' of the anchor. */
  dependsOnFrom: string
  /** The writer pinned this date, so a cascade leaves it alone. */
  dateLocked: boolean
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
  /** The project's timelines. One of them means there is nothing to choose. */
  timelines: { id: string; name: string }[]
  /** The timeline being shown, or empty for all of them. */
  activeTimelineId: string
  /** Manual events this one could hang off. Never includes itself. */
  anchors: { id: string; title: string }[]
  onSubmit(draft: TimelineEventDraft): void
  onCancel(): void
  onDelete?(): void
}

export function TimelineEventEditor({
  initial,
  timelines,
  activeTimelineId,
  anchors,
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
  // A new event made while looking at one timeline belongs to it - that is
  // what the host does with it, and the ticks have to say so rather than
  // showing the first timeline and filing it somewhere else.
  const [dependsOn, setDependsOn] = useState(initial?.dependsOnEventId ?? '')
  const [offsetDays, setOffsetDays] = useState(String(initial?.dependsOnOffsetDays ?? 0))
  const [dependsFrom, setDependsFrom] = useState(initial?.dependsOnFrom ?? 'start')
  const [dateLocked, setDateLocked] = useState(initial?.dateLocked ?? false)
  const [timelineIds, setTimelineIds] = useState<string[]>(
    initial?.timelineIds?.length
      ? initial.timelineIds
      : initial === null && activeTimelineId !== ''
        ? [activeTimelineId]
        : []
  )
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
      endDate: endDate.trim(),
      timelineIds,
      dependsOnEventId: dependsOn,
      // An unreadable offset means zero rather than a saved NaN.
      dependsOnOffsetDays: Number.parseInt(offsetDays, 10) || 0,
      dependsOnFrom: dependsFrom,
      dateLocked
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
        {/* Dates that follow other dates. Every date used to be independent,
            so moving a siege by a week meant retyping every date that hung off
            it - and the ones that were missed did not announce themselves. */}
        {anchors.length > 0 && (
          <>
            <label className="inspector-label" htmlFor="tl-depends">
              {t('timeline.dependsOn')}
            </label>
            <select
              id="tl-depends"
              className="dialog-input"
              value={dependsOn}
              onChange={(e) => setDependsOn(e.target.value)}
            >
              <option value="">{t('timeline.dependsOnNone')}</option>
              {anchors.map((anchor) => (
                <option key={anchor.id} value={anchor.id}>
                  {anchor.title}
                </option>
              ))}
            </select>
            {dependsOn !== '' && (
              <div className="timeline-depends-row">
                <input
                  className="dialog-input timeline-depends-offset"
                  type="number"
                  aria-label={t('timeline.dependsOffset')}
                  value={offsetDays}
                  onChange={(e) => setOffsetDays(e.target.value)}
                />
                {/* Reads as a sentence: "0 days after its start". A bare number
                    box beside a bare dropdown says nothing on its own. */}
                <span className="timeline-depends-word">{t('timeline.dependsOffset')}</span>
                <select
                  className="dialog-input"
                  aria-label={t('timeline.dependsFrom')}
                  value={dependsFrom}
                  onChange={(e) => setDependsFrom(e.target.value)}
                >
                  <option value="start">{t('timeline.dependsFromStart')}</option>
                  <option value="end">{t('timeline.dependsFromEnd')}</option>
                </select>
              </div>
            )}
          </>
        )}
        {/* Without this the cascade would overwrite a date the writer meant. */}
        <label className="timeline-event-timeline">
          <input
            type="checkbox"
            checked={dateLocked}
            onChange={(e) => setDateLocked(e.target.checked)}
          />
          {t('timeline.dateLocked')}
        </label>
        {/* An event can belong to a character's life and to the world's history
            at once, so this is a set rather than a choice - two copies of one
            event would be two things to keep in step. Only worth showing once
            there is more than one timeline. */}
        {timelines.length > 1 && (
          <>
            <span className="inspector-label">{t('timeline.timelines')}</span>
            <div className="timeline-event-timelines">
              {timelines.map((line, index) => (
                <label key={line.id} className="timeline-event-timeline">
                  <input
                    type="checkbox"
                    checked={
                      timelineIds.length === 0 ? index === 0 : timelineIds.includes(line.id)
                    }
                    onChange={(e) => {
                      // An empty set means the first timeline, so the first tick
                      // starts from that rather than from nothing.
                      const current = timelineIds.length === 0 ? [timelines[0].id] : timelineIds
                      setTimelineIds(
                        e.target.checked
                          ? [...current, line.id]
                          : current.filter((id) => id !== line.id)
                      )
                    }}
                  />
                  {line.name}
                </label>
              ))}
            </div>
          </>
        )}
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
