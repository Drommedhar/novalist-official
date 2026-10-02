import { useTranslation } from 'react-i18next'
import { useDictation, startDictation, stopDictation, resumeDictation, discardDictation, showDictation, canDictate,
  updateDictationOptions, refreshMicrophones } from './dictationStore'
import './dictation.css'
import { useShellStore } from '../stores/shellStore'

export function DictationBar(): React.JSX.Element | null {
  const { t } = useTranslation()
  const state = useDictation()
  if (!state.open) return null
  const busy = state.recording || state.starting || state.pending > 0
  const configuring = state.refreshingMicrophones
  const provider = state.providers.find((p) => p.id === state.providerId)
  const system = provider?.id === 'novalist.system'
  const ready = canDictate(provider, state.language)
  return <section className="dictation-bar" aria-label={t('dictation.title')}>
    <div className="dictation-controls">
      {!provider?.usesSystemPanel && <label>{t('dictation.language')}
        <select aria-label={t('dictation.language')} value={state.language} disabled={busy} onChange={(e) => useDictation.setState({ language: e.target.value as 'en' | 'de' })}>
          <option value="en">English</option><option value="de">Deutsch</option>
        </select>
      </label>}
      {state.providers.length > 1 && <label>{t('dictation.provider')}
        <select aria-label={t('dictation.provider')} value={state.providerId} disabled={busy} onChange={(e) => useDictation.setState({ providerId: e.target.value })}>
          {state.providers.map((p) => <option key={p.id} value={p.id}>{p.id === 'novalist.system'
            ? t(p.usesSystemPanel ? 'dictation.windowsName' : 'dictation.systemName') : p.name}</option>)}
        </select>
      </label>}
      {!provider?.usesSystemPanel && provider?.automaticDialogue !== false && <label>{t('dictation.formatting')}
        <select aria-label={t('dictation.formatting')} value={state.formattingMode} disabled={busy} onChange={event => updateDictationOptions({
          formattingMode: event.target.value as 'automatic' | 'plain' })}>
          <option value="automatic">{t('dictation.automaticDialogue')}</option>
          <option value="plain">{t('dictation.plainTranscription')}</option>
        </select>
      </label>}
      {state.recording || state.starting
        ? <button onClick={() => void stopDictation()}>{t('dictation.stop')}</button>
        : state.pending > 0
          ? <button disabled={!state.error && !state.paused} onClick={() => void resumeDictation()}>{t('dictation.resume')}</button>
          : <button disabled={!ready || configuring} onMouseDown={(e) => e.preventDefault()} onClick={() => void startDictation()}>{t(provider?.usesSystemPanel ? 'dictation.openWindows' : 'dictation.start')}</button>}
      {!busy && <button onClick={() => void showDictation()}>{t('dictation.refresh')}</button>}
      {!busy && <button onClick={() => useShellStore.getState().openSettings(system ? 'writingAssistance' : 'extensions')}>{t('dictation.settings')}</button>}
      {state.pending > 0 && !state.recording && <button onClick={() => void discardDictation()}>{t('dictation.discard')}</button>}
      {!busy && <button onClick={() => useDictation.setState({ open: false })}>{t('dialog.close')}</button>}
      <span role="status" aria-live="polite">{state.starting ? t('dictation.starting') : state.recording ? t('dictation.listening') : ''}
        {state.warming && ` ${t('dictation.warming')}`}
        {state.pending > 0 && ` ${t('dictation.pending', { count: state.pending })}`}</span>
      {!provider?.usesSystemPanel && <label className="dictation-level">{t('dictation.inputLevel')}
        <meter min={0} max={1} value={state.inputLevel} aria-label={t('dictation.inputLevel')} />
      </label>}
    </div>
    {!provider?.usesSystemPanel && <details className="dictation-options">
      <summary>{t('dictation.recordingOptions')}</summary>
      <div className="dictation-controls">
        <label>{t('dictation.microphone')}
          <select aria-label={t('dictation.microphone')} value={window.novalist.systemMicrophone ? '' : state.microphoneId} disabled={busy || configuring || !!window.novalist.systemMicrophone}
            onChange={event => updateDictationOptions({ microphoneId: event.target.value })}>
            <option value="">{t('dictation.defaultMicrophone')}</option>
            {!window.novalist.systemMicrophone && state.microphoneId && !state.microphones.some(device => device.id === state.microphoneId)
              && <option value={state.microphoneId}>{t('dictation.unavailableMicrophone')}</option>}
            {state.microphones.map((device, index) => <option key={device.id} value={device.id}>
              {device.label || t('dictation.unnamedMicrophone', { number: index + 1 })}</option>)}
          </select>
        </label>
        {!window.novalist.systemMicrophone && <button disabled={busy || configuring} onClick={() => void refreshMicrophones(true)}>
          {t('dictation.refreshMicrophones')}</button>}
        <label><input type="checkbox" checked={state.voiceCommands} disabled={busy}
          onChange={event => updateDictationOptions({ voiceCommands: event.target.checked })} />{t('dictation.voiceCommands')}</label>
      </div>
      {state.voiceCommands && <p>{t('dictation.commandHelp')}</p>}
      {provider?.supportsVocabulary && <p>{t('dictation.codexVocabulary')}</p>}
    </details>}
    {!ready && <p>{t(system ? 'dictation.systemNeedsSetup' : 'dictation.setup')}</p>}
    {system && <p>{t(provider?.usesSystemPanel ? 'dictation.windowsHelp' : 'dictation.appleHelp')}</p>}
    {provider?.available && !system && !busy && <p>{t(state.formattingMode === 'plain' ? 'dictation.plainDestination' : 'dictation.destinations',
      { audio: provider.audioDestination, formatting: provider.formattingDestination })}</p>}
    {state.error && <p role="alert">{t(state.error)}</p>}
    {state.warning && <p role="status">{t(state.warning)}</p>}
  </section>
}
