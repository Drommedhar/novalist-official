import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import type { SceneEditDto } from './sceneEdit'
import './shellDialogs.css'

interface SceneDialogProps {
  /** Present = edit an existing scene; absent = create a new one. */
  edit?: { chapterGuid: string; sceneId: string; title: string }
  /** Chapter the new scene lands in (create mode). */
  defaultChapterGuid?: string | null
  onClose(): void
}

/** Create or edit a scene: title, POV, and (on create) the target chapter.
 * POV is persisted through scenes/setPov. Scenes have no status field in the
 * data model, so status is intentionally not offered here. */
export function SceneDialog({
  edit,
  defaultChapterGuid,
  onClose
}: SceneDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const isEdit = !!edit
  const [title, setTitle] = useState(edit?.title ?? '')
  const [pov, setPov] = useState('')
  const [initialPov, setInitialPov] = useState('')
  const [chapterGuid, setChapterGuid] = useState(
    edit?.chapterGuid ?? defaultChapterGuid ?? chapters[0]?.guid ?? ''
  )
  const [busy, setBusy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => inputRef.current?.focus(), [])

  useEffect(() => {
    if (!edit) return
    let active = true
    void rpc
      .request<SceneEditDto>('project/getSceneEdit', [edit.chapterGuid, edit.sceneId])
      .then((meta) => {
        if (!active) return
        setPov(meta.pov)
        setInitialPov(meta.pov)
      })
    return () => {
      active = false
    }
  }, [edit])

  const submit = async (): Promise<void> => {
    const name = title.trim()
    if (!name || busy) return
    setBusy(true)
    const store = useProjectStore.getState()
    try {
      if (isEdit && edit) {
        if (name !== edit.title) await store.renameScene(edit.chapterGuid, edit.sceneId, name)
        if (pov.trim() !== initialPov)
          await rpc.request('scenes/setPov', [edit.chapterGuid, edit.sceneId, pov.trim()])
      } else {
        if (!chapterGuid) return
        const before = new Set(
          store.chapters.find((c) => c.guid === chapterGuid)?.scenes.map((s) => s.id) ?? []
        )
        const state = await rpc.request<ProjectStateDto>('project/createScene', [chapterGuid, name])
        const created = state.chapters
          .find((c) => c.guid === chapterGuid)
          ?.scenes.find((s) => !before.has(s.id))
        if (created && pov.trim())
          await rpc.request('scenes/setPov', [chapterGuid, created.id, pov.trim()])
        useProjectStore.getState().applyState(state)
      }
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && !busy && onClose()}
    >
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t(isEdit ? 'explorer.renameScene' : 'dialog.newSceneTitle')}
        onKeyDown={(e) => {
          if (e.key === 'Escape' && !busy) onClose()
        }}
      >
        <div className="dialog-title">
          {t(isEdit ? 'explorer.renameScene' : 'dialog.newSceneTitle')}
        </div>

        <label className="inspector-label">{t('dialog.sceneName')}</label>
        <input
          ref={inputRef}
          className="dialog-input"
          value={title}
          placeholder={t('dialog.sceneNameWatermark')}
          onChange={(e) => setTitle(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void submit()}
        />

        {!isEdit && chapters.length > 0 && (
          <>
            <label className="inspector-label">{t('dialog.chapter')}</label>
            <select
              className="dialog-input"
              value={chapterGuid}
              onChange={(e) => setChapterGuid(e.target.value)}
            >
              {chapters.map((c) => (
                <option key={c.guid} value={c.guid}>
                  {c.title}
                </option>
              ))}
            </select>
          </>
        )}

        <label className="inspector-label">{t('context.pov')}</label>
        <input
          className="dialog-input"
          value={pov}
          placeholder={t('common.povWatermark')}
          onChange={(e) => setPov(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void submit()}
        />

        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy || !title.trim() || (!isEdit && !chapterGuid)}
            onClick={() => void submit()}
          >
            {t('dialog.ok')}
          </button>
        </div>
      </div>
    </div>
  )
}
