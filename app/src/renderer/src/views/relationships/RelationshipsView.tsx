import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import { useRef } from 'react'
import { useContentTransition } from '../../shell/useContentTransition'
import './relationships.css'
import { useRelationshipsView } from './relationshipsModel'
import { RelationshipsToolbar } from './RelationshipsToolbar'
import { FamilyTreeCanvas, RelationshipsCanvas } from './RelationshipsCanvas'

export function RelationshipsView(): React.JSX.Element {
  const { fitToGraph, t, search, setSearch, filterGroup, setFilterGroup, availableGroups, filterRole, setFilterRole, availableRoles, types, setTypes, hideWorldBible, setHideWorldBible, rootId, setRootId, allNodes, asTree, depth, setDepth, setAsTree, ancestorDepth, setAncestorDepth, descendantDepth, setDescendantDepth, setTreeHorizontal, treeHorizontal, withScenes, setWithScenes, hasActiveFilter, clearFilters, zoom, viewportRef, pan, setZoom, setPan, dragRef, movedRef, tree, openEntity, recentre, kinship, layout, selected } = useRelationshipsView()
  const detailRef = useRef<HTMLElement>(null)
  useContentTransition(viewportRef, `${asTree}:${treeHorizontal}`)
  useContentTransition(detailRef, selected?.id ?? '')

  return (
    <div className="relationships">
      <DesktopViewActions>
        <button className="dialog-button" onClick={fitToGraph}>
          {t('desktopRefresh.fitGraph')}
        </button>
      </DesktopViewActions>
      <div className="relationships-header">
        <span className="relationships-title">{t('relationships.title')}</span>
        <span className="relationships-hint">{t('relationships.hint')}</span>
      </div>
      <RelationshipsToolbar t={t} search={search} setSearch={setSearch} filterGroup={filterGroup} setFilterGroup={setFilterGroup} availableGroups={availableGroups} filterRole={filterRole} setFilterRole={setFilterRole} availableRoles={availableRoles} types={types} setTypes={setTypes} hideWorldBible={hideWorldBible} setHideWorldBible={setHideWorldBible} rootId={rootId} setRootId={setRootId} allNodes={allNodes} asTree={asTree} depth={depth} setDepth={setDepth} setAsTree={setAsTree} ancestorDepth={ancestorDepth} setAncestorDepth={setAncestorDepth} descendantDepth={descendantDepth} setDescendantDepth={setDescendantDepth} setTreeHorizontal={setTreeHorizontal} treeHorizontal={treeHorizontal} withScenes={withScenes} setWithScenes={setWithScenes} hasActiveFilter={hasActiveFilter} clearFilters={clearFilters} zoom={zoom} />
      <div className="relationships-workspace">
        <div
          ref={viewportRef}
          className="relationships-viewport"
          onWheel={(e) => {
            e.preventDefault()
            const vp = viewportRef.current
            if (!vp) return
            const rect = vp.getBoundingClientRect()
            const cursorX = e.clientX - rect.left
            const cursorY = e.clientY - rect.top
            const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1
            const newZoom = Math.min(4, Math.max(0.2, zoom * factor))
            if (Math.abs(newZoom - zoom) < 1e-6) return
            const gx = (cursorX - pan.x) / zoom
            const gy = (cursorY - pan.y) / zoom
            setZoom(newZoom)
            setPan({ x: cursorX - gx * newZoom, y: cursorY - gy * newZoom })
          }}
          onPointerDown={(e) => {
            dragRef.current = { startX: e.clientX, startY: e.clientY, panX: pan.x, panY: pan.y }
            movedRef.current = false
          }}
          onPointerMove={(e) => {
            const drag = dragRef.current
            if (!drag) return
            if (Math.abs(e.clientX - drag.startX) + Math.abs(e.clientY - drag.startY) > 4)
              movedRef.current = true
            setPan({
              x: drag.panX + e.clientX - drag.startX,
              y: drag.panY + e.clientY - drag.startY
            })
          }}
          onPointerUp={() => {
            dragRef.current = null
          }}
        >
          {tree ? (
            tree.nodes.length === 0 ? (
              <p className="codex-empty">{t('relationships.treeEmpty')}</p>
            ) : (
              <FamilyTreeCanvas tree={tree} zoom={zoom} pan={pan} openEntity={openEntity} recentre={recentre} kinship={kinship} />
            )
          ) : layout.nodes.length === 0 ? (
            <p className="codex-empty">{t('relationships.emptyHint')}</p>
          ) : (
            <RelationshipsCanvas layout={layout} zoom={zoom} pan={pan} t={t} openEntity={openEntity} recentre={recentre} kinship={kinship} />
          )}
        </div>
        {!window.novalist.isMobile && (
          <aside className="relationships-details" ref={detailRef}>
            <h2>{selected?.displayName || t('desktopRefresh.relationshipSelection')}</h2>
            {selected ? (
              <>
                <p>{[selected.role, selected.group].filter(Boolean).join(' · ')}</p>
                <button className="dialog-button" onClick={() => openEntity(selected.id)}>
                  {t('relationships.openArticle', { name: selected.displayName })}
                </button>
                <h3>{t('desktopRefresh.relationships')}</h3>
                {selected.relationships.map((link, index) => {
                  const target = allNodes.find(
                    (node) =>
                      node.id === link.target ||
                      node.name === link.target ||
                      node.displayName === link.target
                  )
                  return (
                    <div className="relationships-detail-link" key={index}>
                      <span>{link.role}</span>
                      {target ? (
                        <button onClick={() => setRootId(target.id)}>{target.displayName}</button>
                      ) : (
                        <strong>{link.target}</strong>
                      )}
                    </div>
                  )
                })}
              </>
            ) : (
              <p>{t('desktopRefresh.relationshipSelectionHint')}</p>
            )}
          </aside>
        )}
      </div>
    </div>
  )
}
