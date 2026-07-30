import { useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight, Plus } from 'lucide-react'
import type { EntitySummary } from '../../stores/codexStore'
import { ContextMenu, type ContextMenuItem } from '../../shell/ContextMenu'
import { useCodexStore } from '../../stores/codexStore'
import { rpc } from '../../rpc/client'

/** Character grouping mode for the navigation column. */
type GroupMode = 'role' | 'group'

interface CodexNavProps {
  entityType: string
  entities: EntitySummary[]
  selectedId: string | null
  onSelect: (id: string) => void
  onCreate: () => void
  onMove: (id: string, toWorldBible: boolean) => void
  onDelete: (entity: EntitySummary) => void
}

function EntityRow({
  entity,
  active,
  depth,
  onSelect,
  onContext,
  drag
}: {
  entity: EntitySummary
  active: boolean
  depth: number
  onSelect: (id: string) => void
  onContext: (e: React.MouseEvent, entity: EntitySummary) => void
  /** Reparenting handlers. Absent for every type but places. */
  drag?: {
    onStart: (id: string) => void
    onDrop: (parentName: string | null) => void
  }
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <button
      className={`codex-row${active ? ' active' : ''}${entity.isWorld ? ' codex-world' : ''}`}
      style={depth > 0 ? { paddingLeft: `calc(var(--nl-space-md) + ${depth * 14}px)` } : undefined}
      draggable={drag !== undefined}
      onDragStart={() => drag?.onStart(entity.id)}
      onDragOver={(e) => drag && e.preventDefault()}
      onDrop={(e) => {
        if (!drag) return
        e.preventDefault()
        e.stopPropagation()
        drag.onDrop(entity.name)
      }}
      onClick={() => onSelect(entity.id)}
      onContextMenu={(e) => onContext(e, entity)}
    >
      {entity.imagePath ? (
        <img className="codex-thumb" src={`novalist-project://nl/${encodeURI(entity.imagePath)}`} alt="" />
      ) : (
        <span className="codex-thumb codex-thumb-empty" aria-hidden="true">
          {entity.name.slice(0, 1).toUpperCase()}
        </span>
      )}
      <span className="codex-row-text">
        <span className="codex-row-name">{entity.name}</span>
        {entity.detail && <span className="codex-row-detail">{entity.detail}</span>}
      </span>
      {entity.gender && <span className="codex-gender">{entity.gender.slice(0, 1).toUpperCase()}</span>}
      {entity.isWorld && <span className="codex-wb">{t('codexHub.worldBadge')}</span>}
      {entity.isWorldBible && <span className="codex-wb">{t('common.wbBadge')}</span>}
    </button>
  )
}

/**
 * Entity navigation column: search, per-type count, character Role/Group
 * grouping, location parent/child tree, and a right-click menu to move
 * entities to/from the World Bible or delete them.
 */
export function CodexNav({
  entityType,
  entities,
  selectedId,
  onSelect,
  onCreate,
  onMove,
  onDelete
}: CodexNavProps): React.JSX.Element {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const dragged = useRef<string | null>(null)
  const [groupMode, setGroupMode] = useState<GroupMode>('role')
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [menu, setMenu] = useState<{ x: number; y: number; entity: EntitySummary } | null>(null)

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return entities
    return entities.filter((e) => e.name.toLowerCase().includes(q))
  }, [entities, search])

  const openContext = (e: React.MouseEvent, entity: EntitySummary): void => {
    e.preventDefault()
    setMenu({ x: e.clientX, y: e.clientY, entity })
  }

  const menuItems = (entity: EntitySummary): ContextMenuItem[] => [
    {
      label: entity.isWorldBible ? t('entityPanel.moveToBook') : t('entityPanel.moveToWorldBible'),
      onClick: () => onMove(entity.id, !entity.isWorldBible)
    },
    // A world is a container rather than a place inside one, and marking it
    // drops whatever parent it had - there is nothing above a world.
    ...(entityType === 'location'
      ? [
          {
            label: entity.isWorld ? t('codexHub.unmarkWorld') : t('codexHub.markAsWorld'),
            onClick: () => {
              void rpc
                .request('entities/setIsWorld', [entity.id, !entity.isWorld])
                .then(() => void useCodexStore.getState().refresh())
            }
          }
        ]
      : []),
    { label: t('explorer.contextDelete'), danger: true, onClick: () => onDelete(entity) }
  ]

  const toggle = (key: string): void =>
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const rowProps = {
    onSelect,
    onContext: openContext
  }

  const renderFlat = (list: EntitySummary[], depth = 0): React.JSX.Element[] =>
    list.map((entity) => (
      <EntityRow
        key={entity.id}
        entity={entity}
        active={selectedId === entity.id}
        depth={depth}
        {...rowProps}
      />
    ))

  const renderGrouped = (): React.JSX.Element[] => {
    const groups = new Map<string, EntitySummary[]>()
    for (const e of filtered) {
      const key = (groupMode === 'group' ? e.group : e.detail) || t('codexHub.ungrouped')
      if (!groups.has(key)) groups.set(key, [])
      groups.get(key)!.push(e)
    }
    return [...groups.entries()]
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([label, members]) => (
        <div key={label} className="codex-group">
          <button className="codex-group-head" onClick={() => toggle(label)}>
            {collapsed.has(label) ? (
              <ChevronRight size={12} strokeWidth={2} />
            ) : (
              <ChevronDown size={12} strokeWidth={2} />
            )}
            {label}
            <span className="codex-group-count">{members.length}</span>
          </button>
          {!collapsed.has(label) && renderFlat(members)}
        </div>
      ))
  }

  /**
   * Drops the dragged place under another, or at the top when the name is null.
   *
   * The backend has the last word: dropping a place into its own subtree would
   * make it its own ancestor, and a cycle has no root, so the whole branch
   * would silently vanish from the tree. A refused move simply does not happen.
   */
  const reparent = (parentName: string | null): void => {
    const id = dragged.current
    dragged.current = null
    if (!id) return
    const child = entities.find((e) => e.id === id)
    if (!child || child.name === parentName) return
    void rpc
      .request<boolean>('entities/setParent', [id, parentName])
      .then((moved) => moved && void useCodexStore.getState().refresh())
  }

  const renderTree = (): React.JSX.Element[] => {
    const byName = new Map(filtered.map((e) => [e.name.toLowerCase(), e]))
    const childrenOf = new Map<string, EntitySummary[]>()
    const roots: EntitySummary[] = []
    for (const e of filtered) {
      const parentKey = e.parent?.toLowerCase()
      // A world is a root whatever its parent says. Nothing is above a world,
      // which is what makes it one.
      if (!e.isWorld && parentKey && parentKey !== e.name.toLowerCase() && byName.has(parentKey)) {
        if (!childrenOf.has(parentKey)) childrenOf.set(parentKey, [])
        childrenOf.get(parentKey)!.push(e)
      } else {
        roots.push(e)
      }
    }
    // Worlds head the list: they are the containers everything else sits in.
    roots.sort((a, b) => Number(b.isWorld ?? false) - Number(a.isWorld ?? false))
    const render = (entity: EntitySummary, depth: number, seen: Set<string>): React.JSX.Element[] => {
      if (seen.has(entity.id)) return []
      seen.add(entity.id)
      const kids = childrenOf.get(entity.name.toLowerCase()) ?? []
      return [
        <EntityRow
          key={entity.id}
          entity={entity}
          active={selectedId === entity.id}
          depth={depth}
          drag={{ onStart: (id) => (dragged.current = id), onDrop: reparent }}
          {...rowProps}
        />,
        ...kids.flatMap((k) => render(k, depth + 1, seen))
      ]
    }
    const seen = new Set<string>()
    return roots.flatMap((r) => render(r, 0, seen))
  }

  let body: React.JSX.Element[]
  if (filtered.length === 0) body = []
  else if (entityType === 'character') body = renderGrouped()
  else if (entityType === 'location') body = renderTree()
  else body = renderFlat(filtered)

  return (
    <div className="codex-list">
      <div className="codex-nav-head">
        <input
          className="dialog-input codex-search"
          placeholder={t('codexHub.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <span className="codex-count">{filtered.length}</span>
      </div>
      {/* Somewhere to drop a place that should not be inside anything. Without
          it a place could go down the tree and never come back up. */}
      {entityType === 'location' && (
        <div
          className="codex-drop-root"
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => {
            e.preventDefault()
            reparent(null)
          }}
        >
          {t('codexHub.dropToTopLevel')}
        </div>
      )}
      {entityType === 'character' && (
        <div className="codex-groupmode">
          <button
            className={`codex-groupmode-btn${groupMode === 'role' ? ' active' : ''}`}
            onClick={() => setGroupMode('role')}
          >
            {t('entityPanel.groupByRole')}
          </button>
          <button
            className={`codex-groupmode-btn${groupMode === 'group' ? ' active' : ''}`}
            onClick={() => setGroupMode('group')}
          >
            {t('entityPanel.groupByGroup')}
          </button>
        </div>
      )}
      <div className="codex-nav-scroll">
        {body}
        {filtered.length === 0 && <p className="codex-empty">{t('codexHub.emptyHint')}</p>}
      </div>
      <button className="binder-rail-item" onClick={onCreate}>
        <Plus size={14} strokeWidth={2} />
        {t('codexHub.newEntry')}
      </button>
      {menu && (
        <ContextMenu
          x={menu.x}
          y={menu.y}
          items={menuItems(menu.entity)}
          onClose={() => setMenu(null)}
        />
      )}
    </div>
  )
}
