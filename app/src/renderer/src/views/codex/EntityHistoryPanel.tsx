import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { useCodexStore } from '../../stores/codexStore'

/**
 * What this entry said before its last few saves.
 *
 * The panel is shared with plot threads and research items, because a writer
 * should not have to learn a second way to undo. Only what to call and what to
 * do with the answer differs.
 */
export function EntityHistoryPanel({
  entityType,
  entityId
}: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  return (
    <RevisionsPanel
      historyMethod="entities/history"
      restoreMethod="entities/restoreRevision"
      targetId={entityId}
      restoreArgs={[entityType, entityId]}
      onRestored={(record) =>
        useCodexStore.setState({ selectedRecord: record as Record<string, unknown> })
      }
    />
  )
}
