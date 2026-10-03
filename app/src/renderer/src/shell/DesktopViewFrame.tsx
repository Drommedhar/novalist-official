import { createContext, useContext, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { type MainView, useShellStore } from '../stores/shellStore'

const ActionTarget = createContext<HTMLElement | null>(null)
const headings: Partial<Record<MainView, [string, string]>> = {
  manuscript: ['shell.view.manuscript', 'desktopRefresh.views.manuscript'],
  drafts: ['shell.view.drafts', 'desktopRefresh.views.drafts'],
  narration: ['shell.view.narration', 'desktopRefresh.views.narration'],
  timeline: ['shell.view.timeline', 'desktopRefresh.views.timeline'],
  plotGrid: ['shell.view.plotGrid', 'desktopRefresh.views.plotGrid'],
  calendar: ['shell.view.calendar', 'desktopRefresh.views.calendar'],
  relationships: ['shell.view.relationships', 'desktopRefresh.views.relationships'],
  dialogue: ['shell.view.dialogue', 'desktopRefresh.views.dialogue'],
  canvas: ['shell.view.canvas', 'desktopRefresh.views.canvas'],
  style: ['shell.view.style', 'desktopRefresh.views.style'],
  codex: ['shell.view.codex', 'desktopRefresh.views.codex'],
  wiki: ['shell.view.wiki', 'desktopRefresh.views.wiki'],
  maps: ['shell.view.maps', 'desktopRefresh.views.maps'],
  languages: ['shell.view.languages', 'desktopRefresh.views.languages'],
  research: ['shell.view.research', 'desktopRefresh.views.research'],
  gallery: ['shell.view.gallery', 'desktopRefresh.views.gallery'],
  series: ['shell.view.series', 'desktopRefresh.views.series'],
  expose: ['shell.view.expose', 'desktopRefresh.views.expose'],
  export: ['shell.view.export', 'desktopRefresh.views.export'],
  git: ['shell.view.git', 'desktopRefresh.views.git'],
  extensions: ['shell.view.extensions', 'desktopRefresh.views.extensions']
}

/** A heading and action slot belong to each pane, including split windows. */
export function DesktopViewFrame({
  view,
  children
}: {
  view: MainView
  children: ReactNode
}): React.JSX.Element {
  const { t } = useTranslation()
  const [target, setTarget] = useState<HTMLDivElement | null>(null)
  const extension = useShellStore((s) => s.extView)
  const heading = !window.novalist.isMobile && !extension ? headings[view] : undefined
  return (
    <ActionTarget.Provider value={heading ? target : null}>
      {heading && (
        <header className="desktop-view-heading">
          <div>
            <h1>{t(heading[0])}</h1>
            <p>{t(heading[1])}</p>
          </div>
          <div className="desktop-view-actions" ref={setTarget} />
        </header>
      )}
      {children}
    </ActionTarget.Provider>
  )
}

/** The action keeps its view's real handler and state when placed in the heading. */
export function DesktopViewActions({ children }: { children: ReactNode }): React.JSX.Element {
  const target = useContext(ActionTarget)
  return <>{target ? createPortal(children, target) : children}</>
}
