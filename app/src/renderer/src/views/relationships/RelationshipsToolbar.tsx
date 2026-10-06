import { type RelationshipsViewState } from './relationshipsModel'

/** The entry kinds the graph can show. Characters first; it opens on them. */
const ENTRY_KINDS = ['character', 'location', 'item', 'lore', 'scene'] as const

export function RelationshipsToolbar({ t, search, setSearch, filterGroup, setFilterGroup, availableGroups, filterRole, setFilterRole, availableRoles, types, setTypes, hideWorldBible, setHideWorldBible, rootId, setRootId, allNodes, asTree, depth, setDepth, setAsTree, ancestorDepth, setAncestorDepth, descendantDepth, setDescendantDepth, setTreeHorizontal, treeHorizontal, withScenes, setWithScenes, hasActiveFilter, clearFilters, zoom }: { t: RelationshipsViewState['t']; search: RelationshipsViewState['search']; setSearch: RelationshipsViewState['setSearch']; filterGroup: RelationshipsViewState['filterGroup']; setFilterGroup: RelationshipsViewState['setFilterGroup']; availableGroups: RelationshipsViewState['availableGroups']; filterRole: RelationshipsViewState['filterRole']; setFilterRole: RelationshipsViewState['setFilterRole']; availableRoles: RelationshipsViewState['availableRoles']; types: RelationshipsViewState['types']; setTypes: RelationshipsViewState['setTypes']; hideWorldBible: RelationshipsViewState['hideWorldBible']; setHideWorldBible: RelationshipsViewState['setHideWorldBible']; rootId: RelationshipsViewState['rootId']; setRootId: RelationshipsViewState['setRootId']; allNodes: RelationshipsViewState['allNodes']; asTree: RelationshipsViewState['asTree']; depth: RelationshipsViewState['depth']; setDepth: RelationshipsViewState['setDepth']; setAsTree: RelationshipsViewState['setAsTree']; ancestorDepth: RelationshipsViewState['ancestorDepth']; setAncestorDepth: RelationshipsViewState['setAncestorDepth']; descendantDepth: RelationshipsViewState['descendantDepth']; setDescendantDepth: RelationshipsViewState['setDescendantDepth']; setTreeHorizontal: RelationshipsViewState['setTreeHorizontal']; treeHorizontal: RelationshipsViewState['treeHorizontal']; withScenes: RelationshipsViewState['withScenes']; setWithScenes: RelationshipsViewState['setWithScenes']; hasActiveFilter: RelationshipsViewState['hasActiveFilter']; clearFilters: RelationshipsViewState['clearFilters']; zoom: RelationshipsViewState['zoom'] }): React.JSX.Element {
  return (
    <div className="timeline-toolbar">
      <input
        className="dialog-input relationships-search"
        placeholder={t('relationships.search')}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <select
        className="toolbar-select"
        value={filterGroup}
        onChange={(e) => setFilterGroup(e.target.value)}
      >
        <option value="">{t('relationships.filterGroup')}</option>
        {availableGroups.map((g) => (
          <option key={g} value={g}>
            {g}
          </option>
        ))}
      </select>
      <select
        className="toolbar-select"
        value={filterRole}
        onChange={(e) => setFilterRole(e.target.value)}
      >
        <option value="">{t('relationships.filterRole')}</option>
        {availableRoles.map((r) => (
          <option key={r} value={r}>
            {r}
          </option>
        ))}
      </select>
      <span className="relationships-kinds">
        {t('relationships.entryKinds')}
        {ENTRY_KINDS.map((kind) => (
          <label key={kind} className="relationships-toggle">
            <input
              type="checkbox"
              checked={types.includes(kind)}
              onChange={(e) =>
                setTypes(e.target.checked ? [...types, kind] : types.filter((k) => k !== kind))
              }
            />
            {t(`relationships.kind${kind}`)}
          </label>
        ))}
      </span>
      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={hideWorldBible}
          onChange={(e) => setHideWorldBible(e.target.checked)}
        />
        {t('relationships.hideWorldBible')}
      </label>

      {/* Centring on one entry turns a hairball into an answer. Two hops is
          usually where a family or a faction becomes a visible shape. */}
      <select
        className="dialog-input relationships-filter relationships-root"
        aria-label={t('relationships.centreOn')}
        value={rootId ?? ''}
        onChange={(e) => setRootId(e.target.value || null)}
      >
        <option value="">{t('relationships.wholeWorld')}</option>
        {[...allNodes]
          .filter((n) => n.entityType !== 'scene')
          .sort((a, b) => a.displayName.localeCompare(b.displayName))
          .map((n) => (
            <option key={n.id} value={n.id}>
              {n.displayName}
            </option>
          ))}
      </select>
      {/* Only for the graph: the tree is drawn from every entry and reaches
          as far as its own two generation controls say, so leaving this one
          up in tree view offered a third depth dropdown that did nothing. */}
      {rootId && !asTree && (
        <label className="relationships-field">
          <span>{t('relationships.depth')}</span>
          <select
            className="dialog-input relationships-filter relationships-depth"
            value={String(depth)}
            onChange={(e) => setDepth(Number(e.target.value))}
          >
            {[1, 2, 3, 4].map((d) => (
              <option key={d} value={d}>
                {t('relationships.hops', { count: d })}
              </option>
            ))}
          </select>
        </label>
      )}
      {/* Generations rather than a force layout. Needs a root: a tree with
          no root is a forest, and a forest is what the canvas already is. */}
      <button
        className={`dialog-button${asTree ? ' primary' : ''}`}
        disabled={!rootId}
        title={rootId ? undefined : t('relationships.treeNeedsRoot')}
        onClick={() => setAsTree(!asTree)}
      >
        {t(asTree ? 'relationships.asGraph' : 'relationships.asTree')}
      </button>
      {asTree && rootId && (
        <>
          {/* A writer tracing a line of succession wants ten generations down
              and one up; the same view with both at ten is unreadable. The
              label is drawn rather than only announced: "3 up" beside "3 down"
              beside "2 steps" said nothing about which was which. */}
          <label className="relationships-field">
            <span>{t('relationships.ancestors')}</span>
            <select
              className="dialog-input relationships-filter relationships-depth"
              value={String(ancestorDepth)}
              onChange={(e) => setAncestorDepth(Number(e.target.value))}
            >
              {[0, 1, 2, 3, 5, 10].map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
          </label>
          <label className="relationships-field">
            <span>{t('relationships.descendants')}</span>
            <select
              className="dialog-input relationships-filter relationships-depth"
              value={String(descendantDepth)}
              onChange={(e) => setDescendantDepth(Number(e.target.value))}
            >
              {[0, 1, 2, 3, 5, 10].map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
          </label>
          <button className="dialog-button" onClick={() => setTreeHorizontal(!treeHorizontal)}>
            {t(treeHorizontal ? 'relationships.treeVertical' : 'relationships.treeHorizontal')}
          </button>
        </>
      )}
      {/* The edge that was always known and never drawn: which scenes these
          people are actually in together. */}
      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={withScenes}
          onChange={(e) => {
            setWithScenes(e.target.checked)
            // A scene node is useless with its class filtered out.
            if (e.target.checked && !types.includes('scene')) setTypes([...types, 'scene'])
          }}
        />
        {t('relationships.withScenes')}
      </label>
      {hasActiveFilter && (
        <button className="relationships-clear" onClick={clearFilters}>
          {t('relationships.clearFilters')}
        </button>
      )}
      <div className="toolbar-spacer" />
      <span className="calendar-header-label">{Math.round(zoom * 100)}%</span>
    </div>
  )
}
