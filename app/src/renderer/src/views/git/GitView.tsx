import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowDownToLine, ArrowUpFromLine, RefreshCw, Undo2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'

interface GitStatusDto {
  branchName: string
  hasRemote: boolean
  aheadBy: number
  behindBy: number
  changedFiles: { relativePath: string; status: string; isStaged: boolean }[]
}

export function GitView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [status, setStatus] = useState<GitStatusDto | null>(null)
  const [notRepo, setNotRepo] = useState(false)
  const [message, setMessage] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const [confirmDiscard, setConfirmDiscard] = useState(false)

  const refresh = useCallback(async (): Promise<void> => {
    const result = await rpc.request<GitStatusDto | null>('git/status')
    setStatus(result)
    setNotRepo(result === null)
    setSelected(new Set())
  }, [])

  useEffect(() => {
    if (mainView !== 'git') return
    void refresh()
  }, [mainView, refresh])

  if (notRepo) return <p className="codex-empty">{t('git.notARepo')}</p>
  if (!status) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const chosen = selected.size > 0
    ? [...selected]
    : status.changedFiles.map((f) => f.relativePath)

  const act = async (action: () => Promise<string | null>): Promise<void> => {
    setBusy(true)
    setFeedback(null)
    try {
      const error = await action()
      setFeedback(error ?? t('git.commitSuccess'))
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dashboard gitview">
      <h1 className="dashboard-title">
        {status.branchName}
        {status.hasRemote && (
          <span className="inspector-meta">
            {' '}
            {status.aheadBy > 0 && `+${status.aheadBy}`} {status.behindBy > 0 && `-${status.behindBy}`}
          </span>
        )}
      </h1>
      <div className="timeline-toolbar">
        <button className="toolbar-button toolbar-action" disabled={busy} onClick={() => void refresh()}>
          <RefreshCw size={14} strokeWidth={2} />
          {t('git.refresh')}
        </button>
        <div className="toolbar-spacer" />
        <button
          className="toolbar-button toolbar-action"
          disabled={busy || !status.hasRemote}
          onClick={() => void act(() => rpc.request<string | null>('git/pull'))}
        >
          <ArrowDownToLine size={14} strokeWidth={2} />
          {t('git.pull')}
        </button>
        <button
          className="toolbar-button toolbar-action"
          disabled={busy || !status.hasRemote}
          onClick={() => void act(() => rpc.request<string | null>('git/push'))}
        >
          <ArrowUpFromLine size={14} strokeWidth={2} />
          {t('git.push')}
        </button>
      </div>

      <div className="dashboard-card">
        <div className="dashboard-card-title">{t('git.changedFiles')}</div>
        {status.changedFiles.length === 0 && <p className="codex-empty">{t('git.noChanges')}</p>}
        {status.changedFiles.map((file) => (
          <label key={file.relativePath} className="relationships-toggle git-file">
            <input
              type="checkbox"
              checked={selected.size === 0 || selected.has(file.relativePath)}
              onChange={(e) => {
                const next = new Set(
                  selected.size === 0 ? status.changedFiles.map((f) => f.relativePath) : selected
                )
                if (e.target.checked) next.add(file.relativePath)
                else next.delete(file.relativePath)
                setSelected(next.size === status.changedFiles.length ? new Set() : next)
              }}
            />
            <span className="git-file-status" data-status={file.status}>
              {file.status.slice(0, 1)}
            </span>
            <span className="git-file-path">{file.relativePath}</span>
          </label>
        ))}
        {status.changedFiles.length > 0 && (
          <>
            <input
              className="dialog-input git-message"
              placeholder={t('git.commitPlaceholder')}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
            />
            <div className="dialog-actions">
              <button
                className="dialog-button danger"
                disabled={busy}
                onClick={() => setConfirmDiscard(true)}
              >
                <Undo2 size={13} strokeWidth={2} /> {t('git.discardUnstaged')}
              </button>
              <button
                className="dialog-button primary"
                disabled={busy || message.trim().length === 0}
                onClick={() =>
                  void act(() =>
                    rpc.request<string | null>('git/commit', [chosen, message.trim()])
                  ).then(() => setMessage(''))
                }
              >
                {t('git.commitAll')}
              </button>
            </div>
          </>
        )}
        {feedback && <p className="inspector-meta export-result">{feedback}</p>}
      </div>
      {confirmDiscard && (
        <ConfirmDialog
          title={t('git.discardUnstaged')}
          message={t('git.confirmDiscard', { count: chosen.length })}
          onCancel={() => setConfirmDiscard(false)}
          onConfirm={() => {
            setConfirmDiscard(false)
            void act(() => rpc.request<string | null>('git/discard', [chosen]))
          }}
        />
      )}
    </div>
  )
}
