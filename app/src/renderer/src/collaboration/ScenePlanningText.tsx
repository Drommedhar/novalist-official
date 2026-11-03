import { useProjectStore } from '../stores/projectStore'
import { MarkdownEditor } from '../shell/MarkdownEditor'

export function ScenePlanningText({ sceneId, field, label, value, rows = 1, children }: {
  sceneId: string; field: 'synopsis' | 'notes' | 'goal' | 'outcome'; label: string; value: string;
  rows?: number; children: React.ReactNode
}): React.JSX.Element {
  const prose = useProjectStore(state => state.chapters.flatMap(chapter => chapter.scenes).find(scene => scene.id === sceneId)?.sharedProse)
  const itemId = prose?.fields[field]
  if (!prose || !itemId) return <>{children}</>
  return <MarkdownEditor key={itemId} shared={{ sessionId: prose.sessionId, itemId }} value={value}
    ariaLabel={label} minRows={rows} showToolbar={false} onChange={() => {}} />
}
