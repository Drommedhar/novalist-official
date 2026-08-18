import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'

/**
 * The one question asked whenever a screen with unsaved edits is left.
 *
 * Raised by the store rather than by each screen, because the writer leaves
 * through the activity bar, the palette, a hotkey, the binder and a plugin,
 * and a prompt each of those has to remember to raise is a prompt that will be
 * missed by whichever door ships next. Renders nothing until a navigation is
 * actually being held.
 *
 * Keeping the work is on offer, not just losing it: the commonest reason to
 * click away mid-edit is that the edit was finished and the writer moved on.
 */
export function UnsavedLeaveDialog(): React.JSX.Element | null {
  const { t } = useTranslation()
  const pending = useShellStore((s) => s.pendingLeave)
  const [saving, setSaving] = useState(false)

  if (!pending) return null

  const resolve = async (action: 'cancel' | 'discard' | 'save'): Promise<void> => {
    setSaving(action === 'save')
    try {
      await useShellStore.getState().resolveLeave(action)
    } finally {
      setSaving(false)
    }
  }

  return (
    // No backdrop dismissal. This dialog exists because a stray click already
    // cost the writer an edit once.
    <div className="dialog-overlay">
      <div className="dialog-card" role="dialog" aria-label={t('unsavedLeave.title')}>
        <div className="dialog-title">{t('unsavedLeave.title')}</div>
        <p className="dialog-message">{t('unsavedLeave.message', { name: pending.label })}</p>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={() => void resolve('cancel')}>
            {t('unsavedLeave.stay')}
          </button>
          <button className="dialog-button danger" onClick={() => void resolve('discard')}>
            {t('unsavedLeave.discard')}
          </button>
          <button
            className="dialog-button primary"
            disabled={saving}
            onClick={() => void resolve('save')}
          >
            {t('unsavedLeave.saveAndLeave')}
          </button>
        </div>
      </div>
    </div>
  )
}
