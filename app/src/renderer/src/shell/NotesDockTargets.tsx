import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import type { DockEdge } from '../stores/shellStore'
import { dockSceneNotesAt, useNotesDocking } from './sceneNotesDocking'

export function NotesDockTargets({ paneId }: { paneId: string }): React.JSX.Element | null {
  const { t } = useTranslation()
  const { placing, transferring } = useNotesDocking()
  useEffect(() => {
    if (!placing) return
    const escape = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') { event.stopPropagation(); useNotesDocking.setState({ placing: false }) }
    }
    window.addEventListener('keydown', escape, true)
    return () => window.removeEventListener('keydown', escape, true)
  }, [placing])
  if (!placing) return null
  return <div className="notes-dock-targets" role="group" aria-label={t('sceneChecklist.choosePosition')}>
    {(['top', 'left', 'right', 'bottom'] as DockEdge[]).map((edge) => <button key={edge}
      className={`notes-dock-target notes-dock-${edge}`} disabled={transferring}
      onClick={() => void dockSceneNotesAt(paneId, edge)}>{t(`sceneChecklist.dock${edge[0].toUpperCase()}${edge.slice(1)}`)}</button>)}
    <button className="notes-dock-cancel btn-secondary" disabled={transferring} onClick={() => useNotesDocking.setState({ placing: false })}>{t('dialog.cancel')}</button>
  </div>
}
