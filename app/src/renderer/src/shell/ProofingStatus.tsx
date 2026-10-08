import { Check, LoaderCircle, SpellCheck, TextCursorInput } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import './proofing-status.css'

/** The active editor owns the counts and issue targets, including in splits. */
export function ProofingStatus(): React.JSX.Element | null {
  const { t } = useTranslation()
  const status = useEditorBridge((state) => state.proofingStatus)
  const editor = useEditorBridge((state) => state.editor)
  const sceneId = useEditorBridge((state) => state.sceneId)
  const openSceneId = useProjectStore((state) => state.openSceneId)
  const unavailable = useProjectStore((state) =>
    !state.isLoaded || state.workspaceBusy || state.closingProject || state.workspaceSuspended)
  if (unavailable || !editor || !sceneId || sceneId !== openSceneId || !status?.enabled) return null

  const clear = !status.checking && status.grammarCount === 0 && status.spellingCount === 0
  const grammarLabel = t('statusBar.proofingGrammar', { count: status.grammarCount })
  const spellingLabel = t('statusBar.proofingSpelling', { count: status.spellingCount })
  return (
    <span className="status-proofing" role="group" aria-label={t('settings.grammarCheck')}>
      {status.checking && (
        <span className="status-proofing-checking" role="status"
          title={t('statusBar.proofingChecking')} aria-label={t('statusBar.proofingChecking')}>
          <LoaderCircle aria-hidden="true" />
        </span>
      )}
      {clear && (
        <span className="status-proofing-clear" role="status"
          title={t('statusBar.proofingClear')} aria-label={t('statusBar.proofingClear')}>
          <Check aria-hidden="true" />
        </span>
      )}
      {status.grammarCount > 0 && (
        <button type="button" data-proofing-kind="grammar" title={grammarLabel} aria-label={grammarLabel}
          onClick={() => useEditorBridge.getState().editor?.scrollToNextIssue('grammar')}>
          <TextCursorInput aria-hidden="true" />
          <span>{status.grammarCount.toLocaleString()}</span>
        </button>
      )}
      {status.spellingCount > 0 && (
        <button type="button" data-proofing-kind="spelling" title={spellingLabel} aria-label={spellingLabel}
          onClick={() => useEditorBridge.getState().editor?.scrollToNextIssue('punctuation')}>
          <SpellCheck aria-hidden="true" />
          <span>{status.spellingCount.toLocaleString()}</span>
        </button>
      )}
    </span>
  )
}
