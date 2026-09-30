import { useTranslation } from 'react-i18next'
import { useDictation, startDictation, stopDictation, resumeDictation, discardDictation, showDictation } from './dictationStore'
import './dictation.css'
import { useShellStore } from '../stores/shellStore'

export function DictationBar(): React.JSX.Element | null {
  const { t } = useTranslation()
  const state = useDictation()
  if (!state.open) return null
  const busy = state.recording || state.starting || state.pending > 0
  const provider = state.providers.find((p) => p.id === state.providerId)
  return <section className="dictation-bar" aria-label={t('dictation.title')}>
    <div className="dictation-controls">
      <label>{t('dictation.language')}
        <select value={state.language} disabled={busy} onChange={(e) => useDictation.setState({ language: e.target.value as 'en' | 'de' })}>
          <option value="en">English</option><option value="de">Deutsch</option>
        </select>
      </label>
      {state.providers.length > 1 && <label>{t('dictation.provider')}
        <select value={state.providerId} disabled={busy} onChange={(e) => useDictation.setState({ providerId: e.target.value })}>
          {state.providers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
      </label>}
      {state.recording || state.starting
        ? <button onClick={() => void stopDictation()}>{t('dictation.stop')}</button>
        : state.pending > 0
          ? <button disabled={!state.error && !state.paused} onClick={() => void resumeDictation()}>{t('dictation.resume')}</button>
          : <button disabled={!provider?.available} onMouseDown={(e) => e.preventDefault()} onClick={() => void startDictation()}>{t('dictation.start')}</button>}
      {!busy && <button onClick={() => void showDictation()}>{t('dictation.refresh')}</button>}
      {!busy && <button onClick={() => useShellStore.getState().openSettings('extensions')}>{t('dictation.settings')}</button>}
      {state.pending > 0 && !state.recording && <button onClick={() => void discardDictation()}>{t('dictation.discard')}</button>}
      {!busy && <button onClick={() => useDictation.setState({ open: false })}>{t('dialog.close')}</button>}
      <span role="status" aria-live="polite">{state.starting ? t('dictation.starting') : state.recording ? t('dictation.listening') : ''}
        {state.pending > 0 && ` ${t('dictation.pending', { count: state.pending })}`}</span>
    </div>
    {!provider?.available && <p>{t('dictation.setup')}</p>}
    {provider?.available && !busy && <p>{t('dictation.destinations', { audio: provider.audioDestination, formatting: provider.formattingDestination })}</p>}
    {state.error && <p role="alert">{t(state.error)}</p>}
    {state.warning && <p role="status">{t(state.warning)}</p>}
  </section>
}
