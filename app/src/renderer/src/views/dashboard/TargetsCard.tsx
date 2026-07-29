import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useTargetStore, type WordTarget } from '../../stores/targetStore'
import { useProjectStore } from '../../stores/projectStore'
import { InputDialog } from '../../shell/InputDialog'

/** Acts before chapters before scenes, so the card reads top-down like the book. */
const ORDER: Record<WordTarget['kind'], number> = { act: 0, chapter: 1, scene: 2 }

/**
 * Every word target the writer has set, and a place to set one.
 *
 * Targets could only be reached by right-clicking a row in the binder, which
 * meant a writer who had not thought to try that never learned they existed.
 * The dashboard is where the daily and project goals already live, so a target
 * belongs beside them.
 */
export function TargetsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const targets = useTargetStore((s) => s.targets)
  const chapters = useProjectStore((s) => s.chapters)
  const [pending, setPending] = useState<
    | { kind: 'chapter'; guid: string; title: string; current: string }
    | { kind: 'act'; name: string; current: string }
    | { kind: 'scene'; guid: string; sceneId: string; title: string; current: string }
    | null
  >(null)
  const [choice, setChoice] = useState('')

  useEffect(() => {
    void useTargetStore.getState().load()
  }, [])

  // Only what the writer set. A chapter whose figure is the sum of its scenes
  // is already shown by those scenes, and listing both reads as a double count.
  const explicit = [...targets]
    .filter((target) => target.explicit)
    .sort((a, b) => ORDER[a.kind] - ORDER[b.kind] || a.title.localeCompare(b.title))

  const acts = [...new Set(chapters.map((c) => c.act).filter((a) => a))]

  /** Everything that could take a target but has none yet. */
  const settable: { value: string; label: string }[] = [
    ...acts
      .filter((act) => !explicit.some((e) => e.kind === 'act' && e.id === act))
      .map((act) => ({ value: `act:${act}`, label: `${t('targets.kindAct')}: ${act}` })),
    ...chapters
      .filter((c) => !explicit.some((e) => e.kind === 'chapter' && e.id === c.guid))
      .map((c) => ({ value: `chapter:${c.guid}`, label: `${t('targets.kindChapter')}: ${c.title}` }))
  ]

  const openChoice = (): void => {
    const [kind, ...rest] = choice.split(':')
    const id = rest.join(':')
    if (kind === 'act') setPending({ kind: 'act', name: id, current: '' })
    if (kind === 'chapter') {
      const chapter = chapters.find((c) => c.guid === id)
      if (chapter) setPending({ kind: 'chapter', guid: id, title: chapter.title, current: '' })
    }
  }

  const openExisting = (target: WordTarget): void => {
    const current = String(target.target)
    if (target.kind === 'act') {
      setPending({ kind: 'act', name: target.id, current })
      return
    }
    if (target.kind === 'chapter') {
      setPending({ kind: 'chapter', guid: target.id, title: target.title, current })
      return
    }
    const owner = chapters.find((c) => c.scenes.some((s) => s.id === target.id))
    if (owner) {
      setPending({
        kind: 'scene',
        guid: owner.guid,
        sceneId: target.id,
        title: target.title,
        current
      })
    }
  }

  const apply = (value: string): void => {
    const p = pending
    setPending(null)
    setChoice('')
    if (!p) return
    // Zero clears the target, the same gesture the binder's menu uses.
    const words = Number(value) || null
    const store = useTargetStore.getState()
    if (p.kind === 'act') void store.setAct(p.name, words)
    if (p.kind === 'chapter') void store.setChapter(p.guid, words)
    if (p.kind === 'scene') void store.setScene(p.guid, p.sceneId, words)
  }

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('targets.dashboardTitle')}</div>
      <div className="dashboard-echo-desc">{t('targets.dashboardHint')}</div>

      {explicit.length === 0 && <div className="dashboard-echo-desc">{t('targets.empty')}</div>}

      {explicit.map((target) => (
        <button
          key={`${target.kind}:${target.id}`}
          type="button"
          className="dashboard-status-row dashboard-target-row"
          onClick={() => openExisting(target)}
          title={t('targets.progress', {
            words: target.words.toLocaleString(),
            target: target.target.toLocaleString()
          })}
        >
          <span className="dashboard-status-name">
            {t(`targets.kind${target.kind === 'act' ? 'Act' : target.kind === 'chapter' ? 'Chapter' : 'Scene'}`)}
            {': '}
            {target.title}
          </span>
          <div className="dashboard-bar-track dashboard-status-track">
            <div
              className="dashboard-bar-fill"
              style={{
                width: `${Math.min(100, Math.round((target.words / Math.max(1, target.target)) * 100))}%`
              }}
            />
          </div>
          <span className="dashboard-status-count">
            {target.words.toLocaleString()} / {target.target.toLocaleString()}
          </span>
        </button>
      ))}

      {settable.length > 0 && (
        <div className="dashboard-target-add">
          <select
            className="inspector-input"
            value={choice}
            onChange={(e) => setChoice(e.target.value)}
            aria-label={t('targets.addPlaceholder')}
          >
            <option value="">{t('targets.addPlaceholder')}</option>
            {settable.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <button className="dashboard-cover-btn" disabled={choice === ''} onClick={openChoice}>
            {t('targets.add')}
          </button>
        </div>
      )}

      {pending && (
        <InputDialog
          title={t('targets.prompt')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={apply}
        />
      )}
    </div>
  )
}
