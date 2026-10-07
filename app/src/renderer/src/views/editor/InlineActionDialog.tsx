import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { create } from 'zustand'
import { useDialogKeyboard } from '../../shell/useDialogKeyboard'
import type { InlineActionResult } from './editorBridge'

interface Review {
  result: InlineActionResult
  finish(text: string | null): void
}
const useReview = create<{ review: Review | null }>(() => ({ review: null }))

export function reviewInlineAction(result: InlineActionResult, apply: (text: string | null) => void): void {
  useReview.getState().review?.finish(null)
  useReview.setState({ review: { result, finish: (text) => {
    useReview.setState({ review: null })
    apply(text)
  } } })
}

export function InlineActionDialog(): React.JSX.Element | null {
  const review = useReview((state) => state.review)
  return review ? <InlineActionReview key={review.result.text} review={review} /> : null
}

function InlineActionReview({ review }: { review: Review }): React.JSX.Element {
  const { t } = useTranslation()
  const [choice, setChoice] = useState<string | null>(null)
  const keyboard = useDialogKeyboard(() => review.finish(null))
  const { result } = review
  const information = result.disposition === 'information' || !!result.error
  const choices = [...new Set([result.text, ...result.alternatives ?? []])].filter(Boolean)
  return <div className="dialog-overlay">
    <div {...keyboard} className="dialog-card" role="dialog" aria-modal="true" aria-label={t('inlineReview.title')}>
      <div className="dialog-title">{t('inlineReview.title')}</div>
      {information ? <p role={result.error ? 'alert' : undefined} className="dialog-message">{result.error || result.text}</p> :
        <fieldset>
          <legend>{t('inlineReview.choose')}</legend>
          {choices.map((text) => <label key={text} className="dialog-message">
            <input type="radio" name="inline-choice" checked={choice === text} onChange={() => setChoice(text)} />
            {text}
          </label>)}
        </fieldset>}
      <div className="dialog-actions">
        <button className="dialog-button" onClick={() => review.finish(null)}>{t(information ? 'dialog.close' : 'dialog.cancel')}</button>
        {!information && <button className="dialog-button primary" disabled={choice === null} onClick={() => review.finish(choice)}>{t('inlineReview.apply')}</button>}
      </div>
    </div>
  </div>
}
