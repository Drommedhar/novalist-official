import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { ContextPanel } from './ContextPanel'
import { AnnotationsPanel } from './AnnotationsPanel'
import { SceneNotesFields } from './SceneNotesFields'
import { MobileSheet } from './MobileSheet'

type HubTab = 'context' | 'footnotes' | 'notes'

/**
 * Mobile writing hub: the desktop Inspector (Context + Footnotes) and the Scene
 * Notes dock collapse into one swipe-up bottom sheet with three tabs. Raised by
 * the Inspector button in the mobile editor bar.
 */
export function MobileInspectorSheet({
  initialTab = 'context',
  onClose
}: {
  initialTab?: HubTab
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [tab, setTab] = useState<HubTab>(initialTab)
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)

  const tabs: { key: HubTab; label: string }[] = [
    { key: 'context', label: t('context.tab') },
    { key: 'footnotes', label: t('footnotes.tab') },
    { key: 'notes', label: t('sceneNotes.title') }
  ]

  return (
    <MobileSheet title={scene?.title ?? t('shell.inspector')} onClose={onClose}>
      <div className="mobile-sheet-tabs">
        {tabs.map((tabDef) => (
          <button
            key={tabDef.key}
            type="button"
            className={`mobile-sheet-tab${tab === tabDef.key ? ' active' : ''}`}
            onClick={() => setTab(tabDef.key)}
          >
            {tabDef.label}
          </button>
        ))}
      </div>
      <div className="mobile-sheet-tabbody">
        {!(openChapterGuid && openSceneId && scene) ? (
          <div className="inspector-placeholder">{t('shell.inspectorEmpty')}</div>
        ) : (
          <>
            {tab === 'context' && (
              <>
                <div className="inspector-meta">
                  {scene.wordCount.toLocaleString()} {t('shell.words')}
                </div>
                <ContextPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
              </>
            )}
            {tab === 'footnotes' && (
              <AnnotationsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            )}
            {tab === 'notes' && <SceneNotesFields />}
          </>
        )}
      </div>
    </MobileSheet>
  )
}
