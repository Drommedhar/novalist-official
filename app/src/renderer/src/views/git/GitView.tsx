import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowDownToLine, ArrowUpFromLine, Minus, Plus, RefreshCw, Undo2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { InputDialog } from '../../shell/InputDialog'
import './git.css'

interface GitFileDto {
  relativePath: string
  status: string
  isStaged: boolean
}

interface GitStatusDto {
  branchName: string
  hasRemote: boolean
  aheadBy: number
  behindBy: number
  changedFiles: GitFileDto[]
}

/** One commit in the log. */
interface GitCommitDto {
  sha: string
  shortSha: string
  author: string
  date: string
  subject: string
}

interface GitBranchDto {
  name: string
  isCurrent: boolean
}

const STATUS_LABEL: Record<string, string> = {
  Modified: 'M',
  Added: 'A',
  Deleted: 'D',
  Renamed: 'R',
  Untracked: '?',
  Conflicted: 'C',
  Ignored: '!'
}

function splitPath(relativePath: string): { dir: string; name: string } {
  const idx = relativePath.lastIndexOf('/')
  if (idx < 0) return { dir: '', name: relativePath }
  return { dir: relativePath.slice(0, idx), name: relativePath.slice(idx + 1) }
}

export function GitView(): React.JSX.Element {
  const { t } = useTranslation()
  const [status, setStatus] = useState<GitStatusDto | null | undefined>(undefined)
  const [gitInstalled, setGitInstalled] = useState(true)
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const [confirmDiscard, setConfirmDiscard] = useState(false)
  const [log, setLog] = useState<GitCommitDto[]>([])
  const [openCommit, setOpenCommit] = useState<string | null>(null)
  const [commitFiles, setCommitFiles] = useState<string[]>([])
  const [diff, setDiff] = useState<{ path: string; text: string } | null>(null)
  const [branches, setBranches] = useState<GitBranchDto[]>([])
  const [newBranch, setNewBranch] = useState(false)

  const refresh = useCallback(async (): Promise<void> => {
    const result = await rpc.request<GitStatusDto | null>('git/status')
    if (result === null) {
      setGitInstalled(await rpc.request<boolean>('git/installed'))
      setLog([])
      setBranches([])
    } else {
      // The history is why a writer opens this view at all; loading it with
      // the status means it is never a second click away.
      setLog(await rpc.request<GitCommitDto[]>('git/log', [30]).catch(() => []))
      setBranches(await rpc.request<GitBranchDto[]>('git/branches').catch(() => []))
    }
    setStatus(result)
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  if (status === undefined) {
    return <div className="main-placeholder">{t('shell.backendConnecting')}</div>
  }

  if (status === null) {
    return (
      <div className="main-placeholder">
        <h1>{gitInstalled ? t('git.notARepo') : t('git.notInstalled')}</h1>
        <p>{gitInstalled ? t('git.notARepoHint') : t('git.notInstalledHint')}</p>
        {gitInstalled && (
          <button
            className="dialog-button primary"
            disabled={busy}
            onClick={() => {
              setBusy(true)
              void rpc
                .request<string | null>('git/init')
                .then((error) => setFeedback(error))
                .finally(() => {
                  setBusy(false)
                  void refresh()
                })
            }}
          >
            {t('git.init')}
          </button>
        )}
        {feedback && <p className="inspector-meta">{feedback}</p>}
      </div>
    )
  }

  const staged = status.changedFiles.filter((f) => f.isStaged)
  const unstaged = status.changedFiles.filter((f) => !f.isStaged)
  const allPaths = status.changedFiles.map((f) => f.relativePath)
  const unstagedPaths = unstaged.map((f) => f.relativePath)

  // Surfaces the backend error string only; refreshes on success.
  const mutate = async (action: () => Promise<string | null>): Promise<void> => {
    setBusy(true)
    try {
      const error = await action()
      if (error) setFeedback(error)
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  // Shows a success message (or backend error) as feedback.
  const act = async (
    action: () => Promise<string | null>,
    successMessage: string
  ): Promise<void> => {
    setBusy(true)
    setFeedback(null)
    try {
      const error = await action()
      setFeedback(error ?? successMessage)
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  const renderFile = (file: GitFileDto): React.JSX.Element => {
    const { dir, name } = splitPath(file.relativePath)
    return (
      <div key={file.relativePath} className="git-file-row">
        <span className="git-file-status" data-status={file.status}>
          {STATUS_LABEL[file.status] ?? file.status.slice(0, 1)}
        </span>
        <div className="git-file-main">
          <span className="git-file-name">{name}</span>
          {dir && <span className="git-file-dir">{dir}</span>}
        </div>
        {file.isStaged ? (
          <button
            className="git-file-action"
            title={t('git.unstage')}
            aria-label={t('git.unstage')}
            disabled={busy}
            onClick={() => void mutate(() => rpc.request('git/unstage', [[file.relativePath]]))}
          >
            <Minus size={14} strokeWidth={2} />
          </button>
        ) : (
          <button
            className="git-file-action"
            title={t('git.stage')}
            aria-label={t('git.stage')}
            disabled={busy}
            onClick={() => void mutate(() => rpc.request('git/stage', [[file.relativePath]]))}
          >
            <Plus size={14} strokeWidth={2} />
          </button>
        )}
      </div>
    )
  }

  const openCommitFiles = (sha: string): void => {
    setDiff(null)
    if (openCommit === sha) {
      setOpenCommit(null)
      setCommitFiles([])
      return
    }
    setOpenCommit(sha)
    void rpc
      .request<string[]>('git/commitFiles', [sha])
      .then(setCommitFiles)
      .catch(() => setCommitFiles([]))
  }

  const showDiff = (sha: string | null, path: string): void => {
    void rpc
      .request<string>('git/diff', [sha, path])
      .then((text) => setDiff({ path, text }))
      .catch(() => setDiff({ path, text: '' }))
  }

  return (
    <div className="dashboard gitview">
      <h1 className="dashboard-title">
        {status.branchName}
        {status.hasRemote && (status.aheadBy > 0 || status.behindBy > 0) && (
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
          onClick={() => void act(() => rpc.request<string | null>('git/pull'), t('git.pullSuccess'))}
        >
          <ArrowDownToLine size={14} strokeWidth={2} />
          {t('git.pull')}
        </button>
        <button
          className="toolbar-button toolbar-action"
          disabled={busy || !status.hasRemote}
          onClick={() => void act(() => rpc.request<string | null>('git/push'), t('git.pushSuccess'))}
        >
          <ArrowUpFromLine size={14} strokeWidth={2} />
          {t('git.push')}
        </button>
      </div>

      <div className="dashboard-card">
        {status.changedFiles.length === 0 && <p className="codex-empty">{t('git.noChanges')}</p>}

        {staged.length > 0 && (
          <div className="git-section">
            <div className="git-section-header">
              <span className="git-section-title">{t('git.stagedChanges')}</span>
              <button
                className="git-inline-btn"
                disabled={busy}
                onClick={() => void mutate(() => rpc.request('git/unstageAll'))}
              >
                {t('git.unstageAll')}
              </button>
            </div>
            {staged.map(renderFile)}
          </div>
        )}

        {unstaged.length > 0 && (
          <div className="git-section">
            <div className="git-section-header">
              <span className="git-section-title">{t('git.changedFiles')}</span>
              <button
                className="git-inline-btn"
                disabled={busy}
                onClick={() => void mutate(() => rpc.request('git/stageAll'))}
              >
                {t('git.stageAll')}
              </button>
            </div>
            {unstaged.map(renderFile)}
          </div>
        )}

        {status.changedFiles.length > 0 && (
          <>
            <textarea
              className="dialog-input git-message-box"
              placeholder={t('git.commitPlaceholder')}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
            />
            <div className="dialog-actions">
              <button
                className="dialog-button danger"
                disabled={busy || unstagedPaths.length === 0}
                onClick={() => setConfirmDiscard(true)}
              >
                <Undo2 size={13} strokeWidth={2} /> {t('git.discardUnstaged')}
              </button>
              <button
                className="dialog-button"
                disabled={busy || staged.length === 0 || message.trim().length === 0}
                onClick={() =>
                  void act(
                    () => rpc.request<string | null>('git/commitStaged', [message.trim()]),
                    t('git.commitSuccess')
                  ).then(() => setMessage(''))
                }
              >
                {t('git.commitStaged')}
              </button>
              <button
                className="dialog-button primary"
                disabled={busy || message.trim().length === 0}
                onClick={() =>
                  void act(
                    () => rpc.request<string | null>('git/commit', [allPaths, message.trim()]),
                    t('git.commitSuccess')
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
      <div className="dashboard-card">
        <div className="git-section-header">
          <span className="git-section-title">{t('git.branches')}</span>
          <button className="git-inline-btn" disabled={busy} onClick={() => setNewBranch(true)}>
            {t('git.newBranch')}
          </button>
        </div>
        <div className="git-branch-row">
          {branches.map((branch) => (
            <button
              key={branch.name}
              className={`git-inline-btn${branch.isCurrent ? ' active' : ''}`}
              disabled={busy || branch.isCurrent}
              onClick={() =>
                void mutate(() => rpc.request<string | null>('git/switchBranch', [branch.name]))
              }
            >
              {branch.name}
            </button>
          ))}
        </div>
      </div>

      <div className="dashboard-card">
        <div className="git-section-header">
          <span className="git-section-title">{t('git.history')}</span>
        </div>
        {log.length === 0 && <p className="codex-empty">{t('git.noHistory')}</p>}
        {log.map((commit) => (
          <div key={commit.sha}>
            <button className="git-commit-row" onClick={() => openCommitFiles(commit.sha)}>
              <span className="git-commit-subject">{commit.subject}</span>
              <span className="git-commit-meta">
                {commit.shortSha} - {commit.author} - {new Date(commit.date).toLocaleDateString()}
              </span>
            </button>
            {openCommit === commit.sha && (
              <div className="git-commit-files">
                {commitFiles.length === 0 && (
                  <p className="codex-empty">{t('git.noFilesInCommit')}</p>
                )}
                {commitFiles.map((path) => (
                  <button
                    key={path}
                    className="git-commit-file"
                    onClick={() => showDiff(commit.sha, path)}
                  >
                    {path}
                  </button>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>

      {diff && (
        <div className="dashboard-card">
          <div className="git-section-header">
            <span className="git-section-title">{diff.path}</span>
            <button className="git-inline-btn" onClick={() => setDiff(null)}>
              {t('dialog.close')}
            </button>
          </div>
          <pre className="git-diff">
            {diff.text.split('\n').map((line, index) => (
              <span
                key={index}
                className={
                  line.startsWith('+') && !line.startsWith('+++')
                    ? 'git-diff-add'
                    : line.startsWith('-') && !line.startsWith('---')
                      ? 'git-diff-del'
                      : undefined
                }
              >
                {line}
                {'\n'}
              </span>
            ))}
          </pre>
        </div>
      )}

      {newBranch && (
        <InputDialog
          title={t('git.newBranch')}
          placeholder={t('git.branchName')}
          onCancel={() => setNewBranch(false)}
          onSubmit={(name) => {
            setNewBranch(false)
            void mutate(() => rpc.request<string | null>('git/createBranch', [name]))
          }}
        />
      )}

      {confirmDiscard && (
        <ConfirmDialog
          title={t('git.discardUnstaged')}
          message={t('git.confirmDiscard', { count: unstagedPaths.length })}
          onCancel={() => setConfirmDiscard(false)}
          onConfirm={() => {
            setConfirmDiscard(false)
            void mutate(() => rpc.request<string | null>('git/discard', [unstagedPaths]))
          }}
        />
      )}
    </div>
  )
}
