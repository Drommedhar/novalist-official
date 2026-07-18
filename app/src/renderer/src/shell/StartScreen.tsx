import { useTranslation } from 'react-i18next'
import { BookOpen, FolderOpen } from 'lucide-react'

interface StartScreenProps {
  recentProjects: { name: string; path: string }[]
  onOpenPath(path: string): void
  onPickProject(): void
}

export function StartScreen({
  recentProjects,
  onOpenPath,
  onPickProject
}: StartScreenProps): React.JSX.Element {
  const { t } = useTranslation()

  return (
    <div className="start-screen">
      <div className="start-card">
        <BookOpen size={40} strokeWidth={1.25} className="start-logo" />
        <h1>Novalist</h1>
        <button className="start-open" onClick={onPickProject}>
          <FolderOpen size={16} strokeWidth={1.75} />
          {t('welcome.browseFolder')}
        </button>
        <div className="start-recents">
          <div className="start-recents-label">{t('welcome.recentProjects')}</div>
          {recentProjects.length === 0 && (
            <p className="start-recents-empty">{t('welcome.noRecentProjects')}</p>
          )}
          {recentProjects.map((p) => (
            <button key={p.path} className="start-recent" onClick={() => onOpenPath(p.path)}>
              <span className="start-recent-name">{p.name}</span>
              <span className="start-recent-path">{p.path}</span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
