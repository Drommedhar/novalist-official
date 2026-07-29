import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Pause, Play, Square, Timer, Trash2 } from 'lucide-react'
import {
  elapsedSeconds,
  formatDuration,
  sprintWords,
  useSprintStore
} from '../stores/sprintStore'
import './sprint.css'

const TARGETS = [10, 15, 25, 45, 60]

/**
 * A writing sprint: a clock, the words added since it began, and the pace.
 *
 * The smallest unit Novalist had was a calendar day, which cannot answer the
 * only question a writer asks while they are still in the chair.
 */
export function SprintPanel(props: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const running = useSprintStore((s) => s.running)
  const startedAt = useSprintStore((s) => s.startedAt)
  const banked = useSprintStore((s) => s.bankedSeconds)
  const targetMinutes = useSprintStore((s) => s.targetMinutes)
  const history = useSprintStore((s) => s.history)
  const summary = useSprintStore((s) => s.summary)
  // Subscribed so the clock re-renders every second while running.
  useSprintStore((s) => s.tick)

  useEffect(() => {
    void useSprintStore.getState().loadHistory()
  }, [])

  const active = running || banked > 0 || startedAt !== null
  const elapsed = elapsedSeconds()
  const words = sprintWords()
  const remaining = targetMinutes > 0 ? Math.max(0, targetMinutes * 60 - elapsed) : 0
  const pace = elapsed >= 30 ? Math.round((words * 60) / elapsed) : 0

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="dialog-card sprint-card" role="dialog" aria-label={t('sprint.title')}>
        <div className="dialog-title">
          <Timer size={16} /> {t('sprint.title')}
        </div>

        <div className="sprint-clock">
          {targetMinutes > 0 && active
            ? formatDuration(remaining)
            : formatDuration(elapsed)}
        </div>
        <div className="settings-hint">
          {targetMinutes > 0 && active ? t('sprint.remaining') : t('sprint.elapsed')}
        </div>

        <div className="sprint-figures">
          <div>
            <div className="sprint-figure">{words.toLocaleString()}</div>
            <div className="sprint-figure-label">{t('sprint.wordsThisSitting')}</div>
          </div>
          <div>
            {/* Below half a minute a pace figure says more about the
                arithmetic than about the writing. */}
            <div className="sprint-figure">{pace > 0 ? pace : '-'}</div>
            <div className="sprint-figure-label">{t('sprint.wpm')}</div>
          </div>
        </div>

        {!active && (
          <>
            <label className="inspector-label">{t('sprint.target')}</label>
            <div className="settings-button-row">
              {TARGETS.map((minutes) => (
                <button
                  key={minutes}
                  className={`dialog-button${targetMinutes === minutes ? ' active' : ''}`}
                  onClick={() => useSprintStore.getState().setTarget(minutes)}
                >
                  {minutes}
                </button>
              ))}
              <button
                className={`dialog-button${targetMinutes === 0 ? ' active' : ''}`}
                onClick={() => useSprintStore.getState().setTarget(0)}
              >
                {t('sprint.openEnded')}
              </button>
            </div>
          </>
        )}

        <div className="settings-button-row">
          {!active && (
            <button className="dialog-button" onClick={() => useSprintStore.getState().start()}>
              <Play size={14} /> {t('sprint.start')}
            </button>
          )}
          {active && running && (
            <button className="dialog-button" onClick={() => useSprintStore.getState().pause()}>
              <Pause size={14} /> {t('sprint.pause')}
            </button>
          )}
          {active && !running && (
            <button className="dialog-button" onClick={() => useSprintStore.getState().resume()}>
              <Play size={14} /> {t('sprint.resume')}
            </button>
          )}
          {active && (
            <button
              className="dialog-button"
              onClick={() => void useSprintStore.getState().stop()}
            >
              <Square size={14} /> {t('sprint.finish')}
            </button>
          )}
        </div>

        {summary && summary.count > 0 && (
          <>
            <div className="dialog-title">{t('sprint.historyTitle')}</div>
            <div className="settings-hint">
              {t('sprint.summary', {
                count: summary.count,
                words: summary.totalWords.toLocaleString(),
                wpm: summary.averageWordsPerMinute
              })}
            </div>
            <div className="sprint-history">
              {history.slice(0, 10).map((sprint, index) => (
                <div key={index} className="sprint-history-row">
                  <span>{new Date(sprint.startedAt).toLocaleString()}</span>
                  <span>{formatDuration(sprint.seconds)}</span>
                  <span>{sprint.words.toLocaleString()}</span>
                  <span>{sprint.wordsPerMinute > 0 ? sprint.wordsPerMinute : '-'}</span>
                </div>
              ))}
            </div>
            <div className="settings-button-row">
              <button
                className="dialog-button"
                onClick={() => void useSprintStore.getState().clearHistory()}
              >
                <Trash2 size={14} /> {t('sprint.clearHistory')}
              </button>
            </div>
          </>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" onClick={props.onClose}>
            {t('dialog.close')}
          </button>
        </div>
      </div>
    </div>
  )
}
