import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen, RefreshCw, FilePlus2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface LanguagePack {
  code: string
  name: string | null
  /** "bundled" | "user" | "missing" */
  interface: string
  lexicon: string
}

interface Directories {
  themes: string
  locales: string
  analysis: string
}

/**
 * What Novalist can read and write each language in.
 *
 * Novalist bundles three interface languages and three analysis lexicons, and
 * anything beyond that is a file the writer or a contributor drops in. That was
 * true before this panel existed too; the difference is that it was invisible,
 * so a writer working in French had no way to find out why their scenes had no
 * detected emotion, or that they could fix it themselves.
 */
export function LanguagePacksCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [packs, setPacks] = useState<LanguagePack[]>([])
  const [dirs, setDirs] = useState<Directories | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    setPacks(await rpc.request<LanguagePack[]>('appearance/languagePacks'))
    setDirs(await rpc.request<Directories>('appearance/directories'))
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const rescan = async (): Promise<void> => {
    setBusy(true)
    setMessage(null)
    try {
      setPacks(await rpc.request<LanguagePack[]>('appearance/rescan'))
    } finally {
      setBusy(false)
    }
  }

  const startLexicon = async (code: string): Promise<void> => {
    setMessage(null)
    try {
      await rpc.request<string>('appearance/writeLexiconTemplate', [code])
      setMessage(t('languagePacks.templateWritten', { code }))
      await rescan()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  const label = (source: string): string =>
    source === 'bundled'
      ? t('languagePacks.bundled')
      : source === 'user'
        ? t('languagePacks.user')
        : t('languagePacks.missing')

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('languagePacks.intro')}</div>

      <div className="settings-button-row">
        <button className="dialog-button" disabled={busy} onClick={() => void rescan()}>
          <RefreshCw size={14} /> {t('languagePacks.rescan')}
        </button>
        {dirs && (
          <>
            <button
              className="dialog-button"
              onClick={() => void window.novalist.revealPath(dirs.locales)}
            >
              <FolderOpen size={14} /> {t('languagePacks.openLocales')}
            </button>
            <button
              className="dialog-button"
              onClick={() => void window.novalist.revealPath(dirs.analysis)}
            >
              <FolderOpen size={14} /> {t('languagePacks.openAnalysis')}
            </button>
          </>
        )}
      </div>

      {message && <div className="settings-hint">{message}</div>}

      <div className="language-pack-table">
        <div className="language-pack-row language-pack-head">
          <span>{t('languagePacks.language')}</span>
          <span>{t('languagePacks.interfaceColumn')}</span>
          <span>{t('languagePacks.lexiconColumn')}</span>
          <span />
        </div>
        {packs.map((pack) => (
          <div key={pack.code} className="language-pack-row">
            <span className="language-pack-code">
              {pack.code}
              {pack.name && <span className="language-pack-name"> {pack.name}</span>}
            </span>
            <span className={`language-pack-state ${pack.interface}`}>
              {label(pack.interface)}
            </span>
            <span className={`language-pack-state ${pack.lexicon}`}>{label(pack.lexicon)}</span>
            <span>
              {pack.lexicon === 'missing' && (
                <button
                  className="dialog-button"
                  title={t('languagePacks.startLexicon')}
                  onClick={() => void startLexicon(pack.code)}
                >
                  <FilePlus2 size={14} />
                </button>
              )}
            </span>
          </div>
        ))}
      </div>

      <div className="settings-hint">{t('languagePacks.lexiconHint')}</div>
    </div>
  )
}
