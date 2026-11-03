import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import type { ChapterDto, ProjectStateDto } from '../stores/projectTypes'

export function useChapterDates(chapter?: ChapterDto | null) {
  const [start, setStart] = useState(chapter?.dateStart ?? '')
  const [end, setEnd] = useState(chapter?.dateEnd ?? '')
  const [note, setNote] = useState(chapter?.dateNote ?? '')
  const changed = start !== (chapter?.dateStart ?? '')
    || end !== (chapter?.dateEnd ?? '') || note !== (chapter?.dateNote ?? '')
  const save = (guid: string): Promise<ProjectStateDto> =>
    rpc.request('project/setChapterDateRange', [guid, start, end, note])
  return { start, setStart, end, setEnd, note, setNote, changed, save }
}

export function ChapterDateFields({ dates }: { dates: ReturnType<typeof useChapterDates> }): React.JSX.Element {
  const { t } = useTranslation()
  const id = useId()
  return (
    <>
      <div className="inspector-label">{t('explorer.chapterDate')}</div>
      <div className="date-range-row">
        <div className="date-range-col">
          <label className="inspector-label" htmlFor={`${id}-start`}>{t('dialog.dateStart')}</label>
          <input
            id={`${id}-start`}
            className="dialog-input"
            value={dates.start}
            placeholder="2042-03-04 / 812.2.40"
            onChange={(e) => dates.setStart(e.target.value)}
          />
        </div>
        <div className="date-range-col">
          <label className="inspector-label" htmlFor={`${id}-end`}>{t('dialog.dateEnd')}</label>
          <input
            id={`${id}-end`}
            className="dialog-input"
            value={dates.end}
            onChange={(e) => dates.setEnd(e.target.value)}
          />
        </div>
      </div>
      <label className="inspector-label" htmlFor={`${id}-note`}>{t('dialog.dateNoteWatermark')}</label>
      <input
        id={`${id}-note`}
        className="dialog-input"
        value={dates.note}
        onChange={(e) => dates.setNote(e.target.value)}
      />
    </>
  )
}
