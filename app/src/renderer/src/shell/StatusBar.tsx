import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

export function StatusBar(): React.JSX.Element {
  const { t } = useTranslation()
  const backendVersion = useShellStore((s) => s.backendVersion)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const chapters = useProjectStore((s) => s.chapters)
  const openScene = useProjectStore((s) =>
    s.chapters
      .find((c) => c.guid === s.openChapterGuid)
      ?.scenes.find((sc) => sc.id === s.openSceneId)
  )

  const totalWords = chapters.reduce(
    (sum, c) => sum + c.scenes.reduce((s2, sc) => s2 + sc.wordCount, 0),
    0
  )
  const sceneCount = chapters.reduce((sum, c) => sum + c.scenes.length, 0)

  return (
    <footer className="status-bar">
      <span className="status-left">
        {openScene &&
          `${openScene.wordCount.toLocaleString()} ${t('shell.words')} - ${openScene.title}`}
      </span>
      <span className="status-center">
        {isLoaded &&
          `${totalWords.toLocaleString()} ${t('shell.words')} - ${chapters.length} ${t('shell.chapters')} - ${sceneCount} ${t('shell.scenes')}`}
      </span>
      <span className="status-backend">
        {backendVersion
          ? t('shell.backendConnected', { version: backendVersion })
          : t('shell.backendConnecting')}
      </span>
    </footer>
  )
}
