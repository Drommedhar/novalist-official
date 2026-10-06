import { useShellStore } from '../../stores/shellStore'
import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import './narration.css'
import { SegmentPanel } from './NarrationSegmentPanel'
import { DesignDialog } from './NarrationDesignDialog'
import { useNarrationView, type NarrationViewState } from './useNarrationView'
import { NarrationTransport } from './NarrationTransport'
import { NARRATOR } from '../../stores/narrationStore'
import { VoicePicker } from './NarrationVoicePicker'
import { NarratorDesignActions, DesignActions } from './NarrationDesignActions'
import { VoiceScopeButton } from './NarrationVoiceScope'
import { RegisterButton } from './NarrationRegisterButton'
import { MotionPresence } from '../../shell/MotionPresence'

export function NarrationView(): React.JSX.Element {
  const { t, narratorVoiceId, cast, members, voices, designed, setVoice, designer, colours, unassignedCount, noVoices, engines, busy, prepareEngine, frameRef, loading, book, selectedStep, brief, canPlay, speaking, preparing, playFrom, stop, selected, sceneNavigation, readAgain, openSceneId, rate, setRate, readingError, reading } = useNarrationView()

  return (
    <div className="narration-view">
      <DesktopViewActions>
        <button
          className="dialog-button"
          onClick={() => useShellStore.getState().openSettings('narration')}
        >
          {t('desktopRefresh.voiceSettings')}
        </button>
      </DesktopViewActions>
      {!narratorVoiceId && (
        <div className="narration-setup" role="status">
          <div>
            <strong>{t('desktopRefresh.narratorPrompt')}</strong>
            <p>{t('desktopRefresh.narratorHint')}</p>
          </div>
          <button
            className="dialog-button primary"
            onClick={(event) =>
              event.currentTarget
                .closest('.narration-view')
                ?.querySelector<HTMLSelectElement>('.narration-cast select')
                ?.focus()
            }
          >
            {t('desktopRefresh.chooseVoice')}
          </button>
        </div>
      )}
      <div className="narration-workspace">
        <NarrationCast t={t} cast={cast} members={members} voices={voices} designed={designed} narratorVoiceId={narratorVoiceId} setVoice={setVoice} designer={designer} colours={colours} unassignedCount={unassignedCount} noVoices={noVoices} engines={engines} busy={busy} prepareEngine={prepareEngine} />

        <section className="narration-stage" aria-label={t('narration.script')}>
          <iframe
            ref={frameRef}
            className="narration-frame"
            src="./editor/narration-editor.html"
            title={t('narration.script')}
            sandbox="allow-scripts allow-same-origin"
          />
          {loading && !book && <div className="narration-status">{t('narration.loading')}</div>}

          <MotionPresence>{selectedStep && <SegmentPanel step={selectedStep} />}</MotionPresence>

          <MotionPresence>{brief !== null && designer !== null && <DesignDialog brief={brief} />}</MotionPresence>
        </section>
      </div>
      <NarrationTransport canPlay={canPlay} speaking={speaking} preparing={preparing} playFrom={playFrom} stop={stop} t={t} selected={selected} sceneNavigation={sceneNavigation} readAgain={readAgain} openSceneId={openSceneId} frameRef={frameRef} rate={rate} setRate={setRate} readingError={readingError} reading={reading} loading={loading} />
    </div>
  )
}

export function NarrationCast({ t, cast, members, voices, designed, narratorVoiceId, setVoice, designer, colours, unassignedCount, noVoices, engines, busy, prepareEngine }: { t: NarrationViewState['t']; cast: NarrationViewState['cast']; members: NarrationViewState['members']; voices: NarrationViewState['voices']; designed: NarrationViewState['designed']; narratorVoiceId: NarrationViewState['narratorVoiceId']; setVoice: NarrationViewState['setVoice']; designer: NarrationViewState['designer']; colours: NarrationViewState['colours']; unassignedCount: NarrationViewState['unassignedCount']; noVoices: NarrationViewState['noVoices']; engines: NarrationViewState['engines']; busy: NarrationViewState['busy']; prepareEngine: NarrationViewState['prepareEngine'] }): React.JSX.Element {
  return (
    <aside className="narration-cast" aria-label={t('narration.cast')}>
      <div className="narration-cast-title">{t('narration.cast')}</div>
      <div className="narration-cast-head">
        {t('narration.castCount', { cast, total: members.length })}
      </div>

      <ul className="narration-cast-list">
        <li className="narration-cast-row narrator">
          <span className="narration-cast-swatch narration-cast-swatch-narrator" />
          <span className="narration-cast-name">{t('narration.narrator')}</span>
          <VoicePicker
            voices={voices}
            designed={designed}
            value={narratorVoiceId}
            label={t('narration.voiceFor', { name: t('narration.narrator') })}
            onChange={(id) => void setVoice(null, id)}
          />
          {designer !== null && <NarratorDesignActions voiceId={narratorVoiceId} />}
          <span className="narration-cast-marks">
            <RegisterButton characterId={NARRATOR} name={t('narration.narrator')} />
            <VoiceScopeButton characterId={NARRATOR} name={t('narration.narrator')} />
          </span>
        </li>
        {members.map((member) => (
          <li key={member.characterId} className="narration-cast-row">
            <span
              className="narration-cast-swatch"
              style={{ background: colours[member.characterId] }}
            />
            <span className="narration-cast-name" title={member.name}>
              {member.name}
            </span>
            <span className="narration-cast-count">{member.lineCount}</span>
            <VoicePicker
              voices={voices}
              designed={designed}
              value={member.voiceId}
              label={t('narration.voiceFor', { name: member.name })}
              onChange={(id) => void setVoice(member.characterId, id)}
            />
            {designer !== null && (
              <DesignActions characterId={member.characterId} voiceId={member.voiceId} />
            )}
            {/* Together in one cell. Left as separate children of the row's
              grid they landed in its first and last columns, with the
              stretchy middle one holding them a rail's width apart. */}
            <span className="narration-cast-marks">
              <RegisterButton characterId={member.characterId} name={member.name} />
              <VoiceScopeButton characterId={member.characterId} name={member.name} />
            </span>
          </li>
        ))}
      </ul>

      {unassignedCount > 0 && (
        <div className="narration-cast-note">
          {t('narration.unassigned', { count: unassignedCount })}
        </div>
      )}
      {noVoices && <div className="narration-cast-note warn">{t('narration.noVoices')}</div>}

      {/* Every engine, not the first one. Showing only one meant a writer
        who had installed a second could not see it, let alone prepare it -
        and the one they were shown was whichever happened to load first. */}
      {engines.map((engine) => (
        <div key={engine.engineId} className="narration-engine">
          <span className="narration-engine-name">{engine.engineName}</span>
          <span className="narration-engine-state">
            {/* An engine that reports an empty reason has not given one:
              treating "" as a message printed nothing where the state
              should have been. */}
            {engine.isReady
              ? engine.detail.length > 0
                ? engine.detail
                : t('narration.engineReady')
              : engine.error !== null && engine.error.trim().length > 0
                ? engine.error
                : t('narration.engineNotReady')}
          </span>
          {!engine.isReady && (
            <button
              type="button"
              className="narration-prepare"
              disabled={busy}
              onClick={() => void prepareEngine(engine.engineId)}
            >
              {engine.downloadBytes !== null && engine.downloadBytes > 0
                ? t('narration.prepareWithSize', {
                    size: Math.round(engine.downloadBytes / (1024 * 1024 * 1024))
                  })
                : t('narration.prepare')}
            </button>
          )}
        </div>
      ))}
    </aside>
  )
}
