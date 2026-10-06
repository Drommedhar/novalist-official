import { Crosshair, Play, Square } from 'lucide-react'
import { type NarrationWindow } from './useNarrationFrame'
import { type NarrationViewState } from './useNarrationView'

export function NarrationTransport({ canPlay, speaking, preparing, playFrom, stop, t, selected, sceneNavigation, readAgain, openSceneId, frameRef, rate, setRate, readingError, reading, loading }: { canPlay: NarrationViewState['canPlay']; speaking: NarrationViewState['speaking']; preparing: NarrationViewState['preparing']; playFrom: NarrationViewState['playFrom']; stop: NarrationViewState['stop']; t: NarrationViewState['t']; selected: NarrationViewState['selected']; sceneNavigation: NarrationViewState['sceneNavigation']; readAgain: NarrationViewState['readAgain']; openSceneId: NarrationViewState['openSceneId']; frameRef: NarrationViewState['frameRef']; rate: NarrationViewState['rate']; setRate: NarrationViewState['setRate']; readingError: NarrationViewState['readingError']; reading: NarrationViewState['reading']; loading: NarrationViewState['loading'] }): React.JSX.Element {
  return (
    <footer className="narration-transport">
      {/* One button, and it says what it does. The platform engine speaks a
            passage whole, so there is nothing to resume from: stopping ends
            the reading, and starting again starts where you are. */}
      <button
        type="button"
        className="narration-play"
        disabled={!canPlay}
        onClick={() => (speaking === null && !preparing ? playFrom() : stop())}
      >
        {speaking === null && !preparing ? (
          <Play size={13} strokeWidth={1.75} aria-hidden="true" />
        ) : (
          <Square size={13} strokeWidth={1.75} aria-hidden="true" />
        )}
        {/* Making speech takes seconds, and a transport that says nothing
              for that long reads as a button that did not work. */}
        {preparing
          ? t('narration.preparingReading')
          : speaking === null
            ? selected || sceneNavigation
              ? t('narration.playFromHere')
              : t('narration.play')
            : t('narration.stop')}
      </button>

      {/* Delivery is not reproducible: the same line asked for twice comes
            back differently. Reuse is what makes a second listen instant, and
            this is what stops it also making the reading fixed. */}
      <button
        type="button"
        className="narration-again"
        disabled={!canPlay || speaking !== null || preparing}
        title={t('narration.readAgainHint')}
        onClick={() => void readAgain()}
      >
        {t('narration.readAgain')}
      </button>

      <button
        type="button"
        className="narration-locate"
        disabled={!openSceneId}
        onClick={() => {
          const frame = frameRef.current?.contentWindow as NarrationWindow | null
          if (frame && openSceneId) frame.revealScene(openSceneId)
        }}
      >
        <Crosshair size={13} strokeWidth={1.75} aria-hidden="true" />
        {t('narration.goToOpenScene')}
      </button>

      <label className="narration-rate">
        {t('narration.speed')}
        <input
          type="range"
          min={0.5}
          max={2}
          step={0.1}
          value={rate}
          onChange={(e) => setRate(Number(e.target.value))}
        />
        <span className="narration-rate-value">{rate.toFixed(1)}&times;</span>
      </label>

      {/* A reading that ends without a word about it looks exactly like one
            still thinking, which is the state a writer has no way to tell it
            apart from. */}
      {readingError !== null && (
        <span className="narration-transport-note warn">
          {t('narration.readingStopped', { reason: readingError })}
        </span>
      )}
      {!canPlay && reading.length > 0 && (
        <span className="narration-transport-note">{t('narration.nothingCast')}</span>
      )}
      {reading.length === 0 && !loading && (
        <span className="narration-transport-note">{t('narration.emptyBook')}</span>
      )}
    </footer>
  )
}
