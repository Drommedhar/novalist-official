import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Headphones, Square } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useAudiobookStore } from '../../stores/audiobookStore'

/** What a render would cost, before it starts. */
interface EstimateDto {
  chapters: number
  chaptersToRender: number
  scenes: number
  segments: number
  words: number
  audioMs: number
  /** Null when this machine has never finished a render. */
  wallClockMs: number | null
  measured: boolean
  engineId: string | null
  engineName: string | null
}

/** The three ways the finished audio can be delivered. */
const DELIVERIES = [
  { key: 'M4b', labelKey: 'audiobook.deliveryM4b' },
  { key: 'Mp3PerChapter', labelKey: 'audiobook.deliveryMp3' },
  { key: 'WavPerChapter', labelKey: 'audiobook.deliveryWav' }
] as const
type Delivery = (typeof DELIVERIES)[number]['key']

/** A duration as a person reads it, the same shape the backend estimates in. */
function duration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  const pad = (n: number): string => String(n).padStart(2, '0')
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`
}

/**
 * Rendering the book to audio, and packaging what comes out.
 *
 * Sits in the Export view because an audiobook is an edition of the book like
 * any other, and is compiled from exactly the same selection - the chapters
 * that are ticked, the matter pages, the compile-time replacements.
 *
 * What it does that no other format does is take hours. So it says what it is
 * about to cost before it starts, keeps every chapter it finishes, and can be
 * stopped and picked up again where it left off.
 */
export function AudiobookPanel({
  selectedChapterGuids,
  title
}: {
  selectedChapterGuids: string[]
  title: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [estimate, setEstimate] = useState<EstimateDto | null>(null)
  const [delivery, setDelivery] = useState<Delivery>('M4b')
  const [fromScratch, setFromScratch] = useState(false)
  const status = useAudiobookStore((s) => s.status)
  const watch = useAudiobookStore((s) => s.watch)
  const running = status !== null && (status.phase === 'rendering' || status.phase === 'packaging')
  const chapterKey = selectedChapterGuids.join(',')
  const asked = useRef('')

  useEffect(() => {
    // Not while a render is running: the estimate compiles the whole book, and
    // asking for it every few seconds would compete with the render for the
    // machine it is estimating.
    if (running) return
    if (asked.current === chapterKey && estimate !== null) return
    asked.current = chapterKey
    void rpc
      .request<EstimateDto>('audiobook/estimate', [selectedChapterGuids])
      .then(setEstimate)
      .catch(() => setEstimate(null))
  }, [chapterKey, running, selectedChapterGuids, estimate])

  const start = useCallback(async (): Promise<void> => {
    // M4B is one file and is saved as one; the per-chapter deliveries fill a
    // folder, and asking for a file name to put forty files beside is how a
    // delivery ends up somewhere nobody looks.
    const output =
      delivery === 'M4b'
        ? await window.novalist.saveFile(`${title || 'audiobook'}.m4b`)
        : await window.novalist.pickFolder(t('audiobook.pickFolder'))
    if (!output) return

    await rpc.request('audiobook/start', [delivery, output, selectedChapterGuids, 1.0, fromScratch])
    watch()
  }, [delivery, fromScratch, selectedChapterGuids, title, t, watch])

  const stop = useCallback(async (): Promise<void> => {
    await rpc.request('audiobook/stop')
  }, [])

  const percent =
    status !== null && status.segmentsTotal > 0
      ? Math.round((status.segmentsDone / status.segmentsTotal) * 100)
      : 0

  return (
    <div className="export-card audiobook">
      <h3 className="audiobook-heading">
        <Headphones size={16} aria-hidden="true" />
        {t('audiobook.title')}
      </h3>
      <p className="export-preview">{t('audiobook.blurb')}</p>

      {estimate !== null && estimate.engineId === null && (
        <p className="audiobook-warning">{t('audiobook.noEngine')}</p>
      )}

      <div className="export-field">
        <label htmlFor="audiobook-delivery">{t('audiobook.delivery')}</label>
        <select
          id="audiobook-delivery"
          value={delivery}
          disabled={running}
          onChange={(e) => setDelivery(e.target.value as Delivery)}
        >
          {DELIVERIES.map((option) => (
            <option key={option.key} value={option.key}>
              {t(option.labelKey)}
            </option>
          ))}
        </select>
      </div>

      {estimate !== null && (
        <dl className="audiobook-estimate">
          <div>
            <dt>{t('audiobook.chapters')}</dt>
            <dd>
              {estimate.chaptersToRender < estimate.chapters
                ? t('audiobook.chaptersLeft', {
                    left: estimate.chaptersToRender,
                    total: estimate.chapters
                  })
                : estimate.chapters}
            </dd>
          </div>
          <div>
            <dt>{t('audiobook.words')}</dt>
            <dd>{estimate.words.toLocaleString()}</dd>
          </div>
          <div>
            <dt>{t('audiobook.length')}</dt>
            <dd>{duration(estimate.audioMs)}</dd>
          </div>
          <div>
            <dt>{t('audiobook.wallClock')}</dt>
            {/* Never invented. A machine that has rendered nothing yet has
                nothing to base this on, and a made-up figure here is the
                difference between a coffee break and an overnight job. */}
            <dd>
              {estimate.wallClockMs === null
                ? t('audiobook.wallClockUnknown')
                : t('audiobook.wallClockAbout', { time: duration(estimate.wallClockMs) })}
            </dd>
          </div>
        </dl>
      )}

      <label className="audiobook-scratch">
        <input
          type="checkbox"
          checked={fromScratch}
          disabled={running}
          onChange={(e) => setFromScratch(e.target.checked)}
        />
        {t('audiobook.fromScratch')}
      </label>

      {running && status !== null && (
        <div className="audiobook-progress">
          <div className="audiobook-bar" role="progressbar" aria-valuenow={percent}>
            <span style={{ width: `${percent}%` }} />
          </div>
          <p className="export-preview">
            {status.phase === 'packaging'
              ? t('audiobook.packaging')
              : t('audiobook.renderingChapter', {
                  index: status.chapterIndex,
                  total: status.chapterCount,
                  title: status.chapterTitle
                })}
            {' · '}
            {t('audiobook.rendered', { time: duration(status.audioMs) })}
          </p>
        </div>
      )}

      {status !== null && (status.phase === 'done' || status.phase === 'stopped') && (
        <p className="export-result">
          {status.phase === 'done' ? t('audiobook.done') : t('audiobook.stopped')}
          {status.note === 'no-encoder' && ` · ${t('audiobook.noEncoder')}`}
          {status.note === 'encoder-failed' && ` · ${t('audiobook.encoderFailed')}`}
          {status.missing > 0 && ` · ${t('audiobook.missing', { count: status.missing })}`}
        </p>
      )}

      {status !== null && status.phase === 'failed' && (
        <p className="audiobook-warning">
          {status.error === 'no-engine' ? t('audiobook.noEngine') : t('audiobook.failed')}
        </p>
      )}

      <div className="export-run">
        {running ? (
          <button type="button" onClick={() => void stop()}>
            <Square size={14} aria-hidden="true" />
            {t('audiobook.stop')}
          </button>
        ) : (
          <button
            type="button"
            disabled={selectedChapterGuids.length === 0}
            onClick={() => void start()}
          >
            {t('audiobook.render')}
          </button>
        )}
      </div>
    </div>
  )
}
