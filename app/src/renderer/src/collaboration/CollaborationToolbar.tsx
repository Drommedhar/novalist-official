import { useEffect, useRef, useState } from 'react'
import { Users, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from './hostedSessionStore'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useDialogKeyboard } from '../shell/useDialogKeyboard'
import { CollaborationCard } from '../views/settings/CollaborationCard'
import './collaborationToolbar.css'

export function CollaborationToolbar(): React.JSX.Element {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const ready = useShellStore(state => state.backendVersion)
  const view = useShellStore(state => state.mainView)
  const project = useProjectStore(state => state.projectPath)
  const changing = useProjectStore(state => state.workspaceBusy || state.workspaceSuspended || state.closingProject)
  const session = useHostedSessionStore(state => state.session)
  const busy = useHostedSessionStore(state => state.busy)
  useEffect(() => {
    if (!ready || changing) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout>
    const refresh = async (): Promise<void> => {
      await useHostedSessionStore.getState().refresh(() => !stopped)
      if (!stopped) timer = setTimeout(() => void refresh(), 2000)
    }
    void refresh()
    return () => { stopped = true; clearTimeout(timer) }
  }, [ready, project, changing])
  useEffect(() => { setOpen(false) }, [view])
  useEffect(() => {
    if (!open) return
    const outside = (event: PointerEvent): void => {
      const sharing = useHostedSessionStore.getState()
      if (sharing.busy || sharing.hostSynchronizing) return
      if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false)
    }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [open])
  const label = busy ? 'collaboration.working' : session
    ? session.connection === 'Connected' ? session.isOwner ? 'collaboration.sharing' : 'collaboration.joined' : 'collaboration.reconnecting'
    : 'collaboration.open'
  return <div className="collaboration-toolbar" ref={root}>
    <button className={`toolbar-button toolbar-action${session ? ' collaboration-active' : ''}`} data-collaboration-trigger
      aria-label={t('collaboration.title')} aria-expanded={open} aria-haspopup="dialog" disabled={!ready}
      title={session ? t('collaboration.connection.' + session.connection) : t('collaboration.title')}
      onClick={() => setOpen(value => !value)}>
      <Users size={16} strokeWidth={1.75} /><span>{t(label)}</span>
    </button>
    {open && <CollaborationPanel onClose={() => setOpen(false)} />}
  </div>
}

function CollaborationPanel({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const keyboard = useDialogKeyboard(onClose)
  return <div className="collaboration-panel" role="dialog" aria-label={t('collaboration.title')} {...keyboard}>
    <div className="collaboration-panel-heading">
      <h2>{t('collaboration.title')}</h2>
      <button className="toolbar-button" aria-label={t('dialog.close')} onClick={onClose}><X size={16} /></button>
    </div>
    <CollaborationCard />
  </div>
}
