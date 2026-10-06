import {
  useEffect,
  useState
} from 'react'
import { rpc } from '../../rpc/client'




export const QUOTE_LANGUAGES = [
  'en',
  'de-low',
  'de-guillemet',
  'fr',
  'es',
  'it',
  'pt',
  'ru',
  'pl',
  'cs',
  'sk',
  'nl',
  'zh-CN',
  'ja',
  'ko'
]




export function useWritingLanguages(): string[] {
  const [languages, setLanguages] = useState<string[]>(QUOTE_LANGUAGES)
  useEffect(() => {
    let live = true
    void rpc
      .request<string[]>('settings/writingLanguages')
      .then((list) => {
        if (live && list.length > 0) setLanguages(list)
      })
      .catch(() => console.warn('Settings writing-language discovery failed'))
    return () => {
      live = false
    }
  }, [])
  return languages
}





export function useSystemVoices(enabled: boolean): { id: string; name: string; language: string }[] {
  const [voices, setVoices] = useState<{ id: string; name: string; language: string }[]>([])
  useEffect(() => {
    // Settings can be opened while startup is still loading its model. Voice
    // discovery may shell out and the backend dispatches requests serially, so
    // asking for voices first can leave settings/get queued behind it long
    // enough for the screen to look broken. The picker is not renderable until
    // the settings model exists anyway.
    if (!enabled) return
    void rpc
      .request<{ id: string; name: string; language: string }[]>('voices/list')
      .then(setVoices)
      .catch(() => setVoices([]))
  }, [enabled])
  return voices
}



export function useSpeechVoices(): SpeechSynthesisVoice[] {
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([])
  useEffect(() => {
    if (typeof speechSynthesis === 'undefined') return
    const read = (): void => setVoices(speechSynthesis.getVoices())
    read()
    speechSynthesis.addEventListener('voiceschanged', read)
    return () => speechSynthesis.removeEventListener('voiceschanged', read)
  }, [])
  return voices
}
