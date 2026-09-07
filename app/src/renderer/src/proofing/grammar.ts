import { rpc } from '../rpc/client'
import { useSettingsStore } from '../stores/settingsStore'

export interface GrammarIssue {
  message: string
  offset: number
  length: number
  type: 'spelling' | 'grammar' | 'style'
  replacements: string[]
}

export async function checkGrammar(text: string): Promise<GrammarIssue[]> {
  const effective = useSettingsStore.getState().view?.effective
  if (!effective?.grammarCheckEnabled || !text.trim()) return []
  if (effective.grammarCheckProvider !== 'harper') {
    return rpc.request<GrammarIssue[]>('grammar/check', [text])
  }

  // The backend contributes extension rules only when Harper is selected;
  // it must not contact LanguageTool, even if an account is still configured.
  const extensions = rpc.request<GrammarIssue[]>('grammar/check', [text])
  const local = (async () => {
    if (!/^en(?:-|$)/i.test(effective.grammarCheckLanguage)) return []
    const [{ checkHarper }, words] = await Promise.all([
      import('./harper'),
      rpc.request<string[]>('spell/dictionary')
    ])
    return checkHarper(text, effective.grammarCheckLanguage, words)
  })()
  const [issues, extra] = await Promise.all([local, extensions])
  return [...issues, ...extra]
}
