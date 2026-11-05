import { useTranslation } from 'react-i18next'
import { ExternalLink, EyeOff, ListChecks, Move } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { SceneChecklistView } from './SceneChecklistView'
import { floatSceneNotes, hideDockedNotes, placeSceneNotes, useNotesDocking } from './sceneNotesDocking'
import './sceneChecklist.css'

export function SceneNotesPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const { state, transferring } = useNotesDocking()
  const project = useProjectStore()
  const context = state.context
  const valid = context && project.isLoaded && !project.workspaceSuspended &&
    context.projectPath === project.projectPath && context.bookId === project.activeBookId && context.draftId === project.activeDraftId && context.epoch === project.workspaceEpoch &&
    project.chapters.some((chapter) => chapter.guid === context.chapterGuid && chapter.scenes.some((scene) => scene.id === context.sceneId))
  return <section className="scene-notes-panel" inert={transferring} aria-label={t('sceneChecklist.title')}>
    <header className="scene-notes-titlebar">
      <span className="scene-notes-title"><ListChecks size={16} />{t('sceneChecklist.title')}</span>
      <button className="scene-notes-control" title={t('sceneChecklist.move')} aria-label={t('sceneChecklist.move')} onClick={placeSceneNotes}><Move size={16} /></button>
      <button className="scene-notes-control" title={t('sceneChecklist.float')} aria-label={t('sceneChecklist.float')} onClick={() => void floatSceneNotes()}><ExternalLink size={16} /></button>
      <button className="scene-notes-control" title={t('sceneChecklist.hide')} aria-label={t('sceneChecklist.hide')} onClick={() => void hideDockedNotes()}><EyeOff size={16} /></button>
    </header>
    <label className="scene-notes-follow"><input type="checkbox" checked={state.follow} onChange={(event) => {
      const follow = event.target.checked
      useNotesDocking.setState({ state: { ...state, follow } })
      void window.novalist.followSceneNotes?.(follow)
    }} />{t('sceneChecklist.follow')}</label>
    {valid ? <SceneChecklistView key={JSON.stringify(context)} context={context} /> : <p className="scene-checklist-hint">{t('sceneNotes.empty')}</p>}
  </section>
}
