import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { runCommand } from './commands'
import './support.css'

const PREFERENCE_KEY = 'nl.supportNotice.v1'
const REMINDER_DELAY = 30 * 24 * 60 * 60 * 1000

function noticeDue(): boolean {
  const saved = localStorage.getItem(PREFERENCE_KEY)
  if (saved === 'never') return false
  const remindAt = Number(saved)
  return !Number.isFinite(remindAt) || remindAt <= Date.now()
}

export function SupportNotice({ onDismiss }: { onDismiss(): void }): React.JSX.Element | null {
  const { t } = useTranslation()
  const titleId = useId()
  const [visible, setVisible] = useState(noticeDue)

  const dismiss = (preference: string): void => {
    localStorage.setItem(PREFERENCE_KEY, preference)
    setVisible(false)
    onDismiss()
  }

  if (!visible) return null

  return (
    <aside className="support-notice" aria-labelledby={titleId}>
      <div className="support-notice-copy">
        <h2 id={titleId}>{t('support.noticeTitle')}</h2>
        <p>{t('support.noticeMessage')}</p>
      </div>
      <div className="support-notice-actions">
        <button className="dialog-button support-open" type="button" aria-haspopup="dialog" onClick={() => runCommand('app.support')}>
          {t('support.title')}
        </button>
        <button className="support-dismiss" type="button" onClick={() => dismiss(String(Date.now() + REMINDER_DELAY))}>
          {t('support.remindLater')}
        </button>
        <button className="support-dismiss" type="button" onClick={() => dismiss('never')}>
          {t('support.neverAgain')}
        </button>
      </div>
    </aside>
  )
}
