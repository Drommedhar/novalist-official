import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface CompletionListDto {
  words: string[]
  trigger: number
}

/**
 * Words and phrases this book completes as you type.
 *
 * The @-mention picker completes Codex names, in scene prose, and nowhere else.
 * That leaves out everything a secondary world is full of and the Codex is not:
 * a settled spelling of a place, a rank, a coined verb, a phrase that has to
 * read the same way every time. Those get retyped slightly differently, and the
 * inconsistency turns up in copy-edit.
 */
export function CompletionCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [trigger, setTrigger] = useState(3)
  const [dirty, setDirty] = useState(false)

  const apply = (list: CompletionListDto): void => {
    setText((list.words ?? []).join('\n'))
    setTrigger(list.trigger ?? 3)
    setDirty(false)
  }

  useEffect(() => {
    void rpc.request<CompletionListDto>('completion/get').then(apply).catch(() => apply({ words: [], trigger: 3 }))
  }, [])

  const save = (): void => {
    const words = text
      .split('\n')
      .map((w) => w.trim())
      .filter(Boolean)
    void rpc.request<CompletionListDto>('completion/save', [words, trigger]).then(apply)
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('completion.intro')}</div>

      {/* One per line rather than a row of inputs: this is a list somebody
          pastes into and edits in bulk, not a form they fill in. */}
      <textarea
        className="dialog-input token-profile"
        aria-label={t('completion.words')}
        placeholder={t('completion.placeholder')}
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          setDirty(true)
        }}
      />

      <label className="inspector-label" htmlFor="set-completion-trigger">
        {t('completion.trigger')}
      </label>
      <input
        id="set-completion-trigger"
        className="inspector-input"
        type="number"
        min={3}
        max={10}
        value={trigger}
        onChange={(e) => {
          setTrigger(Number(e.target.value) || 3)
          setDirty(true)
        }}
      />
      <div className="settings-hint">{t('completion.triggerHint')}</div>

      <div className="match-row">
        {/* Retyping the whole cast into this box is exactly the work the list
            exists to remove. */}
        <button
          className="btn-secondary"
          onClick={() =>
            void rpc.request<CompletionListDto>('completion/addCodexNames').then(apply)
          }
        >
          {t('completion.addCodexNames')}
        </button>
        <button className="btn-primary" disabled={!dirty} onClick={save}>
          {t('completion.save')}
        </button>
      </div>
    </div>
  )
}
