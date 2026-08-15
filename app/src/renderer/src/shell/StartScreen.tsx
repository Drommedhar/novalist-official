import { useTranslation } from 'react-i18next'
import { BookOpen, CircleHelp, FilePlus2, FolderOpen, Import, Settings } from 'lucide-react'
import { runCommand } from './commands'
import { ScratchpadPanel } from './ScratchpadPanel'

/** How many covers the welcome screen shows. Beyond this the row scrolls. */
const MAX_RECENTS = 8

interface StartScreenProps {
  recentProjects: { name: string; path: string; cover?: string | null }[]
  onOpenPath(path: string): void
}

/**
 * What the content area holds before a project is open.
 *
 * It used to be a screen of its own, shown instead of the shell - which is why
 * Settings had to learn to open without a project as a special case, and why
 * the manual could not be read until you had opened something. It is content
 * now: the same window, the same menu bar, the same rail, with whatever needs
 * a project disabled rather than absent.
 *
 * Every button here runs a registry command, so this is a way in to the app's
 * own commands rather than a second implementation of them.
 */
export function StartScreen({
  recentProjects,
  onOpenPath
}: StartScreenProps): React.JSX.Element {
  const { t } = useTranslation()

  return (
    <div className="start-screen">
      <div className="start-card">
        <BookOpen size={40} strokeWidth={1.25} className="start-logo" />
        <h1>Novalist</h1>
        <button className="start-open" onClick={() => runCommand('app.newProject')}>
          <FilePlus2 size={16} strokeWidth={1.75} />
          {t('welcome.newProject')}
        </button>
        <button className="start-open secondary" onClick={() => runCommand('app.openProject')}>
          <FolderOpen size={16} strokeWidth={1.75} />
          {t('welcome.browseFolder')}
        </button>
        <button className="start-open secondary" onClick={() => runCommand('app.importProject')}>
          <Import size={16} strokeWidth={1.75} />
          {t('welcome.importPlugin')}
        </button>
        <div className="start-system-actions">
          <button className="start-open secondary" onClick={() => runCommand('nav.settings')}>
            <Settings size={16} strokeWidth={1.75} />
            {t('settings.title')}
          </button>
          <button className="start-open secondary" onClick={() => runCommand('app.manual')}>
            <CircleHelp size={16} strokeWidth={1.75} />
            {t('help.title')}
          </button>
        </div>
        <div className="start-recents">
          <div className="start-recents-label">{t('welcome.recentProjects')}</div>
          {recentProjects.length === 0 && (
            <p className="start-recents-empty">{t('welcome.noRecentProjects')}</p>
          )}
          {/* One row, never a wall. The list grows with every project ever
              opened, and a grid that wraps pushed the scratchpad off the
              bottom of the screen by the tenth. */}
          <div className="start-recent-row">
            {recentProjects.slice(0, MAX_RECENTS).map((p) => (
              <button
                key={p.path}
                className="start-recent-card"
                onClick={() => onOpenPath(p.path)}
                title={p.path}
              >
                <div className="start-recent-cover">
                  {p.cover ? (
                    <img className="start-recent-cover-img" src={p.cover} alt="" />
                  ) : (
                    <div className="start-recent-cover-empty">
                      <BookOpen size={28} strokeWidth={1.25} />
                    </div>
                  )}
                </div>
                <span className="start-recent-name">{p.name}</span>
                <span className="start-recent-path">{p.path}</span>
              </button>
            ))}
          </div>
        </div>

        {/* Reachable with no project open, which is the whole point: this is
            where a thought goes when the project it belongs to is not the one
            in front of you. */}
        <ScratchpadPanel canFile={false} />
      </div>
    </div>
  )
}
