import { useSettingsStore } from '../../stores/settingsStore'
import { learnWord } from '../../shell/useSpellCheck'
import { rpc } from '../../rpc/client'
import { checkGrammar } from '../../proofing/grammar'
import { hardBandColors } from './editorConfiguration'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorProofingMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { editorRef } = context
  return {
    grammarCheckRequest: (message) => {
      const editor = editorRef.current
      if (!editor) return
      const requestId = Number(message.requestId)
      void checkGrammar(String(message.plainText ?? ''))
        .then((issues) => {
          if (editorRef.current === editor)
            editor.setGrammarIssues(JSON.stringify(issues), requestId)
        })
        .catch(() => {
          // Offline or endpoint unavailable: clear underlines quietly.
          if (editorRef.current === editor) editor.setGrammarIssues('[]', requestId)
        })
    },
    replaceMisspelling: (message) => {
      window.novalist.replaceMisspelling(String(message.replacement ?? ''))
    },
    addToDictionary: (message) => {
      // This used to reach LanguageTool's dictionary alone, which needs a
      // paid account and left the writer's own list empty and the red
      // underline exactly where it was.
      void learnWord(String(message.word ?? '')).then(() => {
        // Chromium re-checks a live element when its spellcheck attribute
        // changes, so re-pushing the setting is what lifts the underline
        // off the word now instead of at the next keystroke.
        const enabled = useSettingsStore.getState().view?.effective.spellCheckEnabled ?? true
        editorRef.current?.setSpellCheck(enabled)
      })
    },
    readabilityRequest: (message) => {
      void rpc
        .request<{ level: string }[]>('style/sentenceReadability', [
          String(message.plainText ?? '')
        ])
        .then((sentences) => {
          // Only the two hard bands are painted. Tinting every sentence
          // turns the page into a heat map you stop reading; what a writer
          // needs is the handful of sentences that fight the reader.
          const colors = hardBandColors()
          editorRef.current?.setReadability(
            JSON.stringify({
              sentences: sentences.filter((s) => s.level in colors),
              colors
            })
          )
        })
        .catch(() => editorRef.current?.setReadability('{"sentences":[]}'))
    }
  }
}
