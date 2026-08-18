import { MainArea } from './MainArea'
import { StartScreen } from './StartScreen'
import { useShellStore } from '../stores/shellStore'
import './mobile.css'

/**
 * What iOS shows before a project is open.
 *
 * The welcome material is content in the desktop shell rather than a front door
 * of its own, sitting in the content area beside the mode rail. With no project
 * open the phone fell through to that shell and got the desktop layout it was
 * never going to fit: the mode rail drawn as a block across the top of the
 * screen - under the status bar, because the desktop shell holds no safe-area
 * insets - with the welcome card pushed underneath it and the whole screen
 * scrolling as one desktop page.
 *
 * A rail is not a phone control, and the native chrome is deliberately hidden
 * here (setNavVisible is false until a project is open), so this is the same
 * welcome content in the mobile frame instead: safe areas held out by the
 * frame, one column, and nothing around it. The moment a project opens,
 * MobileShell takes over and this is gone.
 *
 * Settings is the one screen reachable from here, and it carries its own way
 * back (SettingsView's phone branch) because there is no rail and no tab bar to
 * leave it by.
 */
export function MobileWelcome({
  recentProjects,
  onOpenPath
}: {
  recentProjects: { name: string; path: string; cover?: string | null }[]
  onOpenPath: (path: string) => void
}): React.JSX.Element {
  const mainView = useShellStore((s) => s.mainView)
  // The views that do not need a project. Everything else here would have
  // nothing to show, so the welcome content stands until one is open.
  const appScopedView =
    mainView === 'settings' || mainView === 'extensions' || mainView === 'about'

  return (
    <div className="mobile-shell">
      <div className="mobile-content">
        {appScopedView ? (
          <MainArea />
        ) : (
          <StartScreen recentProjects={recentProjects} onOpenPath={onOpenPath} />
        )}
      </div>
    </div>
  )
}
