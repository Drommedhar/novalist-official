import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, Plus, RefreshCw } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useBookScope, useProjectStore } from '../stores/projectStore'
import { loadBookScoped } from '../stores/bookScopedLoad'
import { ContextMenu } from './ContextMenu'
import { MotionPresence } from './MotionPresence'
import { ConfirmDialog } from './ConfirmDialog'
import { SmartListEditor, type SmartListDraft } from './SmartListEditor'
import { openBinderScene } from './binderNavigation'

/** One condition in a saved list. */
export interface SmartListRule {
  field: string
  op: string
  value: string
}

export interface SmartListDto {
  id: string
  name: string
  match: 'All' | 'Any'
  rules: SmartListRule[]
}

interface SmartListMatch {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
}

type Pending =
  { kind: 'create' } | { kind: 'edit'; list: SmartListDto } | { kind: 'delete'; list: SmartListDto }

export function SmartListsPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [lists, setLists] = useState<SmartListDto[]>([])
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [matches, setMatches] = useState<Record<string, SmartListMatch[]>>({})
  const [menu, setMenu] = useState<{ x: number; y: number; list: SmartListDto } | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const bookScope = useBookScope()
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)

  useEffect(() => {
    // Saved lists belong to the active book. Loading them once on mount left
    // the panel showing the previous book's lists after a book switch.
    let active = true
    void loadBookScoped(useProjectStore.getState,
      () => rpc.request<SmartListDto[]>('smartLists/list'),
      (items) => { if (active) setLists(items) })
      .catch(() => { if (active) setLists([]) })
    setMatches({})
    setExpanded({})
    return () => { active = false }
  }, [bookScope, workspaceBusy])

  const evaluate = async (id: string): Promise<void> => {
    await loadBookScoped(useProjectStore.getState,
      () => rpc.request<SmartListMatch[]>('smartLists/evaluate', [id]),
      (result) => setMatches((m) => ({ ...m, [id]: result })))
  }

  const toggle = (id: string): void => {
    const willExpand = !expanded[id]
    setExpanded((e) => ({ ...e, [id]: willExpand }))
    if (willExpand && !matches[id]) void evaluate(id)
  }

  const save = async (draft: SmartListDraft, id: string | null): Promise<void> => {
    await loadBookScoped(useProjectStore.getState,
      () => rpc.request<SmartListDto[]>('smartLists/save', [
      id,
      draft.name,
      draft.match,
      draft.rules
      ]), (updated) => { setLists(updated); setMatches({}) })
  }

  return (
    <div className="smartlists">
      <button className="binder-rail-item" onClick={() => setPending({ kind: 'create' })}>
        <Plus size={14} strokeWidth={2} />
        {t('smartList.addNew')}
      </button>
      {lists.map((list) => (
        <div key={list.id}>
          <div
            className="binder-chapter-row"
            onContextMenu={(e) => {
              e.preventDefault()
              setMenu({ x: e.clientX, y: e.clientY, list })
            }}
          >
            <button
              className="binder-expand"
              aria-label={list.name}
              onClick={() => toggle(list.id)}
            >
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${expanded[list.id] ? ' open' : ''}`}
              />
            </button>
            <span className="binder-chapter-title">{list.name}</span>
            {expanded[list.id] && (
              <button
                className="binder-expand"
                aria-label={t('smartList.refresh')}
                onClick={() => void evaluate(list.id)}
              >
                <RefreshCw size={12} strokeWidth={2} />
              </button>
            )}
          </div>
          <MotionPresence collapse disabled={suspendMotion}>{expanded[list.id] && <div>
            {(matches[list.id] ?? []).map((match) => (
              <button
                key={match.sceneId}
                className="binder-scene-row"
                onClick={() => openBinderScene(match.chapterGuid, match.sceneId)}
              >
                <span className="binder-scene-title">
                  {match.chapterTitle} - {match.sceneTitle}
                </span>
              </button>
            ))}
          {(matches[list.id]?.length ?? 0) === 0 && (
            <div className="binder-placeholder">{t('smartList.noMatches')}</div>
          )}
          </div>}</MotionPresence>
        </div>
      ))}
      <MotionPresence disabled={suspendMotion}>{menu && (
        <ContextMenu
          x={menu.x}
          y={menu.y}
          items={[
            {
              label: t('explorer.contextRename'),
              onClick: () => setPending({ kind: 'edit', list: menu.list })
            },
            {
              label: t('explorer.contextDelete'),
              danger: true,
              onClick: () => setPending({ kind: 'delete', list: menu.list })
            }
          ]}
          onClose={() => setMenu(null)}
        />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{pending?.kind === 'create' && (
        <SmartListEditor
          initial={null}
          onCancel={() => setPending(null)}
          onSubmit={(draft) => {
            setPending(null)
            void save(draft, null)
          }}
        />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{pending?.kind === 'edit' && (
        <SmartListEditor
          initial={pending.list}
          onCancel={() => setPending(null)}
          onSubmit={(draft) => {
            const id = pending.list.id
            setPending(null)
            void save(draft, id)
          }}
        />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={pending.list.name}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const id = pending.list.id
            setPending(null)
            void loadBookScoped(useProjectStore.getState,
              () => rpc.request<SmartListDto[]>('smartLists/delete', [id]), setLists)
          }}
        />
      )}</MotionPresence>
    </div>
  )
}
