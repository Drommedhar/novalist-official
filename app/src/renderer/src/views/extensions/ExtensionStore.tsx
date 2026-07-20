import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, Check, Download, RefreshCw, Search } from 'lucide-react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useExtensionsStore, type StoreEntry, type StoreRelease } from '../../stores/extensionsStore'

/**
 * Store tab of the Extensions view: browses the remote extension gallery, shows a
 * per-extension detail panel (README + release notes), and installs/updates
 * extensions. Download progress and Cancel surface through the shared host
 * progress overlay (driven by the backend `ui/progress/*` bridge).
 */
export function ExtensionStore(): React.JSX.Element {
  const { t } = useTranslation()
  const store = useExtensionsStore((s) => s.store)
  const status = useExtensionsStore((s) => s.storeStatus)
  const error = useExtensionsStore((s) => s.storeError)
  const loadStore = useExtensionsStore((s) => s.loadStore)
  const checkStoreUpdates = useExtensionsStore((s) => s.checkStoreUpdates)
  const installFromStore = useExtensionsStore((s) => s.installFromStore)

  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [checking, setChecking] = useState(false)
  const [banner, setBanner] = useState<string | null>(null)
  const [itemError, setItemError] = useState<string | null>(null)

  useEffect(() => {
    void loadStore()
  }, [loadStore])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return store
    const terms = q.split(/\s+/)
    return store.filter((e) =>
      terms.every(
        (term) =>
          e.name.toLowerCase().includes(term) ||
          e.description.toLowerCase().includes(term) ||
          e.author.toLowerCase().includes(term) ||
          e.tags.some((tag) => tag.toLowerCase().includes(term))
      )
    )
  }, [store, query])

  const selected = selectedId ? (store.find((e) => e.id === selectedId) ?? null) : null

  const doInstall = async (entry: StoreEntry): Promise<void> => {
    setBusyId(entry.id)
    setItemError(null)
    try {
      const result = await installFromStore(entry.id, entry.repo, entry.hasUpdate)
      if (!result.success && result.error && result.error !== 'cancelled') {
        setItemError(
          result.error === 'incompatible' ? t('extensions.store.incompatible') : result.error
        )
      }
    } finally {
      setBusyId(null)
    }
  }

  const doCheckUpdates = async (): Promise<void> => {
    setChecking(true)
    setBanner(null)
    try {
      const count = await checkStoreUpdates()
      setBanner(
        count === 0
          ? t('extensions.store.upToDate')
          : count === 1
            ? t('extensions.store.updateAvailableSingle')
            : t('extensions.store.updateAvailableMulti').replace('{0}', String(count))
      )
    } finally {
      setChecking(false)
    }
  }

  if (status === 'loading' || status === 'idle') {
    return <p className="codex-empty">{t('extensions.store.loading')}</p>
  }

  if (status === 'error') {
    return (
      <div className="store-state">
        <p className="extensions-error">{error ?? t('extensions.store.loadError')}</p>
        <button type="button" className="export-inline-btn" onClick={() => void loadStore(true)}>
          <RefreshCw size={13} strokeWidth={2} /> {t('extensions.store.retry')}
        </button>
      </div>
    )
  }

  if (selected) {
    return (
      <StoreDetail
        entry={selected}
        busy={busyId === selected.id}
        onBack={() => {
          setSelectedId(null)
          setItemError(null)
        }}
        onInstall={() => void doInstall(selected)}
        error={itemError}
      />
    )
  }

  return (
    <div className="store-browse">
      <div className="store-toolbar">
        <div className="store-search">
          <Search size={14} strokeWidth={2} aria-hidden />
          <input
            type="text"
            className="store-search-input"
            placeholder={t('extensions.store.search')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>
        <button
          type="button"
          className="export-inline-btn"
          disabled={checking}
          onClick={() => void doCheckUpdates()}
        >
          <RefreshCw size={13} strokeWidth={2} /> {t('extensions.store.checkUpdates')}
        </button>
      </div>

      {banner && <p className="store-banner">{banner}</p>}

      {store.length === 0 ? (
        <p className="codex-empty">{t('extensions.store.empty')}</p>
      ) : filtered.length === 0 ? (
        <p className="codex-empty">{t('extensions.store.noResults')}</p>
      ) : (
        <div className="extensions-list">
          {filtered.map((entry) => (
            <section
              key={entry.id}
              className="dashboard-card extension-card store-card"
              onClick={() => setSelectedId(entry.id)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  setSelectedId(entry.id)
                }
              }}
            >
              <div className="extension-card-main">
                <div className="extension-card-head">
                  <span className="extension-card-name">{entry.name}</span>
                  {entry.latestVersion && (
                    <span className="extension-card-version">{entry.latestVersion}</span>
                  )}
                  <StoreStateChip entry={entry} />
                </div>
                {entry.author && (
                  <div className="extension-card-author">
                    {t('extensions.author')} {entry.author}
                  </div>
                )}
                {entry.description && <p className="extension-card-desc">{entry.description}</p>}
              </div>
              <div className="extension-card-buttons">
                <StoreActionButton
                  entry={entry}
                  busy={busyId === entry.id}
                  onClick={(e) => {
                    e.stopPropagation()
                    void doInstall(entry)
                  }}
                />
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  )
}

function StoreStateChip({ entry }: { entry: StoreEntry }): React.JSX.Element | null {
  const { t } = useTranslation()
  if (!entry.isCompatible) {
    return <span className="extension-card-state">{t('extensions.store.incompatible')}</span>
  }
  if (entry.hasUpdate) {
    return (
      <span className="extension-card-state update">{t('extensions.store.update')}</span>
    )
  }
  if (entry.isInstalled) {
    return <span className="extension-card-state on">{t('extensions.store.installed')}</span>
  }
  return null
}

function StoreActionButton({
  entry,
  busy,
  onClick
}: {
  entry: StoreEntry
  busy: boolean
  onClick: (e: React.MouseEvent) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const disabled = busy || !entry.isCompatible || (entry.isInstalled && !entry.hasUpdate)
  const label = busy
    ? entry.hasUpdate
      ? t('extensions.store.updating')
      : t('extensions.store.installing')
    : entry.hasUpdate
      ? t('extensions.store.update')
      : entry.isInstalled
        ? t('extensions.store.installed')
        : t('extensions.store.install')
  const Icon = entry.isInstalled && !entry.hasUpdate ? Check : Download
  return (
    <button type="button" className="export-inline-btn" disabled={disabled} onClick={onClick}>
      <Icon size={13} strokeWidth={2} /> {label}
    </button>
  )
}

function StoreDetail({
  entry,
  busy,
  onBack,
  onInstall,
  error
}: {
  entry: StoreEntry
  busy: boolean
  onBack: () => void
  onInstall: () => void
  error: string | null
}): React.JSX.Element {
  const { t } = useTranslation()
  const fetchReadme = useExtensionsStore((s) => s.fetchReadme)
  const fetchReleases = useExtensionsStore((s) => s.fetchReleases)
  const [readme, setReadme] = useState<string | null>(null)
  const [releases, setReleases] = useState<StoreRelease[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    setLoading(true)
    void (async () => {
      const [rm, rel] = await Promise.all([
        fetchReadme(entry.repo).catch(() => ''),
        fetchReleases(entry.id, entry.repo).catch(() => [] as StoreRelease[])
      ])
      if (!alive) return
      setReadme(rm)
      setReleases(rel)
      setLoading(false)
    })()
    return () => {
      alive = false
    }
  }, [entry.id, entry.repo, fetchReadme, fetchReleases])

  const latest = releases[0]

  return (
    <div className="store-detail">
      <div className="store-detail-head">
        <button type="button" className="export-inline-btn" onClick={onBack}>
          <ArrowLeft size={13} strokeWidth={2} /> {t('extensions.store.back')}
        </button>
        <div className="store-detail-title">
          <span className="extension-card-name">{entry.name}</span>
          {entry.latestVersion && (
            <span className="extension-card-version">{entry.latestVersion}</span>
          )}
          <StoreStateChip entry={entry} />
        </div>
        <StoreActionButton entry={entry} busy={busy} onClick={() => onInstall()} />
      </div>

      {entry.author && (
        <div className="extension-card-author">
          {t('extensions.author')} {entry.author}
        </div>
      )}
      {error && <p className="extensions-error">{error}</p>}

      {loading ? (
        <p className="codex-empty">{t('extensions.store.loading')}</p>
      ) : (
        <div className="store-markdown">
          {readme ? (
            <Markdown remarkPlugins={[remarkGfm]}>{readme}</Markdown>
          ) : (
            <p className="extension-card-desc">{t('extensions.store.readmeUnavailable')}</p>
          )}
          {latest?.body && (
            <>
              <h2 className="store-notes-title">{t('extensions.store.releaseNotes')}</h2>
              <Markdown remarkPlugins={[remarkGfm]}>{latest.body}</Markdown>
            </>
          )}
        </div>
      )}
    </div>
  )
}
