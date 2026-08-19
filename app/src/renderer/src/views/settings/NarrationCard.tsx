import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'

/** One installed speech engine, as the backend reports it. */
interface VoiceEngine {
  engineId: string
  engineName: string
  isReady: boolean
  isPreparing: boolean
  error: string | null
  detail: string
  downloadBytes: number | null
}

/**
 * The speech engines, in Settings rather than only on the cast rail.
 *
 * Getting an engine ready is an install: a Python environment, a couple of
 * gigabytes of torch, then several more of model weights. It belonged in
 * Settings from the start and lived instead behind a button in the Narration
 * view — which is the one screen that has nothing to show a writer who has not
 * got an engine yet, so the way to fix "no speech engine" was to visit the
 * screen that only works once you have one.
 *
 * Everything installed is listed, not the first. A writer who has two can
 * prepare either; showing one meant the one that happened to load first.
 */
export function NarrationCard(): React.JSX.Element {
  const { t } = useTranslation()
  const setMainView = useShellStore((s) => s.setMainView)
  const [engines, setEngines] = useState<VoiceEngine[] | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const read = async (): Promise<void> => {
    try {
      setEngines(await rpc.request<VoiceEngine[]>('voiceEngines/list'))
    } catch {
      // No backend, or none that knows about engines. An empty list is the
      // truthful answer and is what the copy below is written for.
      setEngines([])
    }
  }

  useEffect(() => {
    void read()
  }, [])

  // While one is loading its model there is nothing to do but wait, and a card
  // that does not change while it happens reads as one that never will.
  const preparing = engines?.some((engine) => engine.isPreparing) ?? false
  useEffect(() => {
    if (!preparing) return
    const timer = setInterval(() => void read(), 2000)
    return () => clearInterval(timer)
  }, [preparing])

  const prepare = async (engineId: string): Promise<void> => {
    setBusy(engineId)
    try {
      await rpc.request('voiceEngines/prepare', [engineId])
    } catch {
      // The engine's own status carries the reason, and it is read below.
    } finally {
      setBusy(null)
      await read()
    }
  }

  return (
    <>
      <div className="settings-hint">{t('settings.narrationDesc')}</div>

      {engines !== null && engines.length === 0 && (
        <div className="settings-hint export-warning">{t('settings.narrationNoEngines')}</div>
      )}

      {engines?.map((engine) => (
        <div key={engine.engineId} className="settings-narration-engine">
          <span className="settings-narration-name">{engine.engineName}</span>
          <span className="settings-narration-state">
            {engine.isReady
              ? engine.detail.length > 0
                ? engine.detail
                : t('settings.narrationReady')
              : engine.isPreparing
                ? t('settings.narrationPreparing')
                : engine.error !== null && engine.error.trim().length > 0
                  ? engine.error
                  : t('settings.narrationNotReady')}
          </span>
          {!engine.isReady && !engine.isPreparing && (
            <button
              type="button"
              className="dialog-button"
              disabled={busy !== null}
              onClick={() => void prepare(engine.engineId)}
            >
              {/* The size, before the wait rather than during it. A writer on a
                  metered connection is entitled to know what they are agreeing
                  to before they agree to it. */}
              {engine.downloadBytes !== null && engine.downloadBytes > 0
                ? t('settings.narrationPrepareWithSize', {
                    size: Math.round(engine.downloadBytes / (1024 * 1024 * 1024))
                  })
                : t('settings.narrationPrepare')}
            </button>
          )}
        </div>
      ))}

      <div className="settings-hint">{t('settings.narrationWhereDesc')}</div>
      <button type="button" className="dialog-button" onClick={() => setMainView('narration')}>
        {t('settings.narrationOpen')}
      </button>
    </>
  )
}
