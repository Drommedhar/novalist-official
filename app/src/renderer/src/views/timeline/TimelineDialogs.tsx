import { MotionPresence } from '../../shell/MotionPresence'
import { rpc } from '../../rpc/client'

import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { InputDialog } from '../../shell/InputDialog'
import { TimelineEventEditor } from './TimelineEventEditor'
import { anchors, type TimelineDto, type timelinePresentation } from './timelineModel'
type TimelineViewState = ReturnType<typeof timelinePresentation>

export function TimelineDialogs({ addingTimeline, t, setAddingTimeline, setData, renamingTimeline, data, setRenamingTimeline, removingTimeline, setRemovingTimeline, pending, setPending, save, manualId }: { addingTimeline: TimelineViewState['addingTimeline']; t: TimelineViewState['t']; setAddingTimeline: TimelineViewState['setAddingTimeline']; setData: TimelineViewState['setData']; renamingTimeline: TimelineViewState['renamingTimeline']; data: TimelineViewState['data']; setRenamingTimeline: TimelineViewState['setRenamingTimeline']; removingTimeline: TimelineViewState['removingTimeline']; setRemovingTimeline: TimelineViewState['setRemovingTimeline']; pending: TimelineViewState['pending']; setPending: TimelineViewState['setPending']; save: TimelineViewState['save']; manualId: TimelineViewState['manualId'] }): React.JSX.Element {
  return <>
      <MotionPresence>{addingTimeline && (
        <InputDialog
          title={t('timeline.addTimeline')}
          onCancel={() => setAddingTimeline(false)}
          onSubmit={(name) => {
            setAddingTimeline(false)
            void rpc.request<TimelineDto>('timeline/addTimeline', [name]).then(setData)
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{renamingTimeline && (
        <InputDialog
          title={t('explorer.contextRename')}
          placeholder={data.timelines.find((l) => l.id === data.activeTimelineId)?.name ?? ''}
          onCancel={() => setRenamingTimeline(false)}
          onSubmit={(name) => {
            setRenamingTimeline(false)
            void rpc
              .request<TimelineDto>('timeline/renameTimeline', [data.activeTimelineId, name])
              .then(setData)
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{removingTimeline && (
        <ConfirmDialog
          title={t('timeline.removeTimeline')}
          message={t('timeline.removeTimelineHint')}
          onCancel={() => setRemovingTimeline(false)}
          onConfirm={() => {
            setRemovingTimeline(false)
            void rpc
              .request<TimelineDto>('timeline/deleteTimeline', [data.activeTimelineId])
              .then(setData)
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{pending?.kind === 'create' && (
        <TimelineEventEditor
          initial={null}
          timelines={data.timelines}
          activeTimelineId={data.activeTimelineId}
          anchors={anchors(data, null)}
          onCancel={() => setPending(null)}
          onSubmit={(draft) => {
            setPending(null)
            void save(draft, null)
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{pending?.kind === 'edit' && (
        <TimelineEventEditor
          initial={pending.event}
          timelines={data.timelines}
          activeTimelineId={data.activeTimelineId}
          anchors={anchors(data, manualId(pending.event))}
          onCancel={() => setPending(null)}
          onDelete={() => {
            const event = pending.event
            setPending({ kind: 'delete', event })
          }}
          onSubmit={(draft) => {
            const id = manualId(pending.event)
            setPending(null)
            void save(draft, id)
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={pending.event.title}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const id = manualId(pending.event)
            setPending(null)
            void rpc.request<TimelineDto>('timeline/deleteEvent', [id]).then(setData)
          }}
        />
      )}</MotionPresence>
  </>
}
