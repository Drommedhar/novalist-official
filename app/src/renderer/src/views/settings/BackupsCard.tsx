import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Archive, FolderOpen, Flag, RotateCcw, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'

export interface BackupDto {
  id: string
  path: string
  createdAt: string
  sizeBytes: number
  trigger: string
  /** A named archive the writer asked to keep. Exempt from retention. */
  isMilestone: boolean
  /** The name they gave it, or empty for an ordinary rotating archive. */
  name: string
}

/** Bytes to a short human string. Archives run from a few KB to hundreds of MB. */
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`
}

export function BackupsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const hasProject = useProjectStore((s) => Boolean(s.projectName))
  const global = useSettingsStore((s) => s.view?.global) ?? {}
  const update = useSettingsStore((s) => s.update)

  const [backups, setBackups] = useState<BackupDto[]>([])
  const [folder, setFolder] = useState('')
  const [busy, setBusy] = useState(false)
  const [milestoneName, setMilestoneName] = useState('')

  const refresh = useCallback(async () => {
    if (!hasProject) {
      setBackups([])
      setFolder('')
      return
    }
    setBackups(await rpc.request<BackupDto[]>('backup/list'))
    setFolder(await rpc.request<string>('backup/folder'))
  }, [hasProject])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const backUpNow = async (): Promise<void> => {
    setBusy(true)
    try {
      await rpc.request<BackupDto | null>('backup/create', ['manual'])
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  const formatDate = (iso: string): string => new Date(iso).toLocaleString()

  const keepThisVersion = async (): Promise<void> => {
    const name = milestoneName.trim()
    if (!name) return
    setBusy(true)
    try {
      await rpc.request<BackupDto | null>('backup/createMilestone', [name])
      setMilestoneName('')
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  const remove = async (backup: BackupDto): Promise<void> => {
    // Retention will never clear a milestone, so deleting one is deliberate and
    // gets asked about rather than assumed.
    const label = backup.name || formatDate(backup.createdAt)
    if (!window.confirm(t('backup.deleteConfirm', { name: label }))) return
    setBusy(true)
    try {
      await rpc.request<boolean>('backup/delete', [backup.id])
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  const prune = async (): Promise<void> => {
    setBusy(true)
    try {
      setBackups(await rpc.request<BackupDto[]>('backup/prune'))
    } finally {
      setBusy(false)
    }
  }

  const restore = async (backup: BackupDto): Promise<void> => {
    // Overwriting the project folder is the most destructive action in the app.
    // The service archives the current state first, so this is undoable, but the
    // user still gets told before it happens rather than after.
    const ok = window.confirm(t('backup.restoreConfirm', { date: formatDate(backup.createdAt) }))
    if (!ok) return
    setBusy(true)
    try {
      await rpc.request<boolean>('backup/restore', [backup.id])
      // The backend reopened the project over restored files; pull the fresh
      // state so the binder is not showing chapters that no longer exist.
      await useProjectStore.getState().flushPendingSave()
      useProjectStore
        .getState()
        .applyState(await rpc.request<ProjectStateDto>('project/getState'))
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={Boolean(global.backupEnabled)}
          onChange={(e) => void update('global', { backupEnabled: e.target.checked })}
        />
        {t('backup.enabled')}
      </label>
      <div className="settings-hint">{t('backup.enabledDesc')}</div>

      <label className="inspector-label" htmlFor="set-backup-interval">
        {t('backup.interval')}
      </label>
      <input
        id="set-backup-interval"
        className="inspector-input"
        type="number"
        min={0}
        max={1440}
        value={Number(global.backupIntervalMinutes ?? 30)}
        onChange={(e) =>
          void update('global', { backupIntervalMinutes: Number(e.target.value) || 0 })
        }
      />
      <div className="settings-hint">{t('backup.intervalDesc')}</div>

      <label className="inspector-label" htmlFor="set-backup-retention">
        {t('backup.retention')}
      </label>
      <input
        id="set-backup-retention"
        className="inspector-input"
        type="number"
        min={1}
        max={100}
        value={Number(global.backupRetentionCount ?? 5)}
        onChange={(e) =>
          void update('global', { backupRetentionCount: Number(e.target.value) || 1 })
        }
      />
      <div className="settings-hint">{t('backup.retentionDesc')}</div>

      <label className="inspector-label" htmlFor="set-backup-folder">
        {t('backup.folder')}
      </label>
      <div className="settings-button-row">
        <input
          id="set-backup-folder"
          className="inspector-input"
          type="text"
          value={String(global.backupFolder ?? '')}
          placeholder={t('backup.folderDefault')}
          onChange={(e) => void update('global', { backupFolder: e.target.value })}
        />
        <button
          className="dialog-button"
          onClick={() => {
            void (async () => {
              const picked = await window.novalist.pickFolder('Backups')
              if (picked) await update('global', { backupFolder: picked })
            })()
          }}
        >
          <FolderOpen size={14} /> {t('backup.browse')}
        </button>
      </div>
      <div className="settings-hint">{t('backup.folderDesc')}</div>

      <div className="settings-button-row">
        <button className="dialog-button" disabled={!hasProject || busy} onClick={() => void backUpNow()}>
          <Archive size={14} /> {t('backup.backUpNow')}
        </button>
        <button className="dialog-button" disabled={!hasProject || busy} onClick={() => void prune()}>
          {t('backup.prune')}
        </button>
        {folder && (
          <button className="dialog-button" onClick={() => void window.novalist.revealPath(folder)}>
            <FolderOpen size={14} /> {t('backup.openFolder')}
          </button>
        )}
      </div>

      <label className="inspector-label" htmlFor="set-backup-milestone">
        {t('backup.milestone')}
      </label>
      <div className="settings-button-row">
        <input
          id="set-backup-milestone"
          className="inspector-input"
          type="text"
          value={milestoneName}
          disabled={!hasProject || busy}
          placeholder={t('backup.milestonePlaceholder')}
          onChange={(e) => setMilestoneName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void keepThisVersion()
          }}
        />
        <button
          className="dialog-button"
          disabled={!hasProject || busy || milestoneName.trim().length === 0}
          onClick={() => void keepThisVersion()}
        >
          <Flag size={14} /> {t('backup.keepVersion')}
        </button>
      </div>
      <div className="settings-hint">{t('backup.milestoneDesc')}</div>

      {!hasProject && <div className="settings-hint">{t('backup.noProject')}</div>}

      {hasProject && (
        <div className="backup-list">
          {backups.length === 0 ? (
            <div className="settings-hint">{t('backup.none')}</div>
          ) : (
            backups.map((b) => (
              <div key={b.id} className={`backup-row${b.isMilestone ? ' backup-row-milestone' : ''}`}>
                <div className="backup-row-main">
                  <span className="backup-row-date">
                    {b.isMilestone && <Flag size={12} aria-hidden />}
                    {b.isMilestone ? b.name : formatDate(b.createdAt)}
                  </span>
                  <span className="backup-row-meta">
                    {b.isMilestone
                      ? `${formatDate(b.createdAt)} · ${t('backup.kept')}`
                      : t(`backup.trigger.${b.trigger}`, { defaultValue: b.trigger })}{' '}
                    · {formatSize(b.sizeBytes)}
                  </span>
                </div>
                <button className="dialog-button" disabled={busy} onClick={() => void restore(b)}>
                  <RotateCcw size={14} /> {t('backup.restore')}
                </button>
                <button
                  className="dialog-button"
                  disabled={busy}
                  title={t('backup.delete')}
                  aria-label={t('backup.delete')}
                  onClick={() => void remove(b)}
                >
                  <Trash2 size={14} />
                </button>
              </div>
            ))
          )}
        </div>
      )}
    </>
  )
}
