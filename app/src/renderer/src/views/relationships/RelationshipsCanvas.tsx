import { NODE_SIZE } from './layout'
import { TREE_NODE_WIDTH, TREE_NODE_HEIGHT } from './familyTree'
import { type RelationshipsViewState } from './relationshipsModel'

// Distinct per-family box colors (cycles when there are more families than
// entries), mirroring the Avalonia RelationshipsGraph palette.
const BOX_PALETTE = [
  '#89b4fa',
  '#a6e3a1',
  '#fab387',
  '#f5c2e7',
  '#94e2d5',
  '#f9e2af',
  '#f38ba8',
  '#cba6f7',
  '#74c7ec',
  '#b4befe'
]

/**
 * The longest label a node box can hold before the text runs out of it.
 *
 * A person's name fits; "Haus der Larsons" and "Halb geschriebenes Notizbuch"
 * do not, and once places and things share the canvas the overflow from two
 * neighbours meets in the middle and neither is readable. SVG text has no
 * ellipsis of its own, so the string is cut and the whole name stays in the
 * tooltip that was already there.
 */
const LABEL_LIMIT = 14

function fitLabel(name: string): string {
  const trimmed = name.trim()
  return trimmed.length <= LABEL_LIMIT ? trimmed : `${trimmed.slice(0, LABEL_LIMIT - 1)}…`
}

/**
 * How round each kind's box is. A silhouette is readable at a glance and at any
 * zoom, where a colour alone stops working once the graph is dense.
 */
const NODE_RADIUS: Record<string, number> = {
  character: 6,
  location: 0,
  item: 15,
  lore: 10,
  scene: 2
}

export function FamilyTreeCanvas({ tree, zoom, pan, openEntity, recentre, kinship }: { tree: NonNullable<RelationshipsViewState['tree']>; zoom: RelationshipsViewState['zoom']; pan: RelationshipsViewState['pan']; openEntity: RelationshipsViewState['openEntity']; recentre: RelationshipsViewState['recentre']; kinship: RelationshipsViewState['kinship'] }): React.JSX.Element {
  return (
    <svg
      className="relationships-canvas"
      width={tree.width * zoom}
      height={tree.height * zoom}
      viewBox={`0 0 ${tree.width} ${tree.height}`}
      style={{ transform: `translate(${pan.x}px, ${pan.y}px)` }}
    >
      {/* Parent to child, drawn under the boxes so a line never crosses
        a name. */}
      {tree.edges.map((edge, i) => {
        const from = tree.nodes.find((n) => n.id === edge.parentId)
        const to = tree.nodes.find((n) => n.id === edge.childId)
        if (!from || !to) return null
        return (
          <line
            key={`${edge.parentId}-${edge.childId}-${i}`}
            className="tree-edge"
            x1={from.x + TREE_NODE_WIDTH / 2}
            y1={from.y + TREE_NODE_HEIGHT / 2}
            x2={to.x + TREE_NODE_WIDTH / 2}
            y2={to.y + TREE_NODE_HEIGHT / 2}
          />
        )
      })}
      {tree.nodes.map((node) => (
        <g
          key={node.id}
          className="tree-node"
          onClick={(e) => (e.altKey ? openEntity(node.id) : recentre(node.id))}
        >
          <rect
            x={node.x}
            y={node.y}
            width={TREE_NODE_WIDTH}
            height={TREE_NODE_HEIGHT}
            rx={6}
            className={`tree-box${node.generation === 0 ? ' root' : ''}`}
          />
          <text
            x={node.x + TREE_NODE_WIDTH / 2}
            y={node.y + TREE_NODE_HEIGHT / 2 + 4}
            className="tree-name"
          >
            {fitLabel(node.name)}
          </text>
          {/* The whole name, and what they are to the root - the same
            answer the graph gives, kept when the shape changes. */}
          <title>
            {kinship[node.id] ? `${node.name} - ${kinship[node.id]}` : node.name}
          </title>
        </g>
      ))}
    </svg>
  )
}

export function RelationshipsCanvas({ layout, zoom, pan, t, openEntity, recentre, kinship }: { layout: RelationshipsViewState['layout']; zoom: RelationshipsViewState['zoom']; pan: RelationshipsViewState['pan']; t: RelationshipsViewState['t']; openEntity: RelationshipsViewState['openEntity']; recentre: RelationshipsViewState['recentre']; kinship: RelationshipsViewState['kinship'] }): React.JSX.Element {
  return (
    <svg
      className="relationships-canvas"
      width={layout.width * zoom}
      height={layout.height * zoom}
      viewBox={`0 0 ${layout.width} ${layout.height}`}
      style={{ transform: `translate(${pan.x}px, ${pan.y}px)` }}
    >
      {layout.boxes.map((box, i) => {
        const color = BOX_PALETTE[i % BOX_PALETTE.length]
        const isRole = box.kind === 'role'
        return (
          <g key={i}>
            <rect
              x={box.x}
              y={box.y}
              width={box.width}
              height={box.height}
              className="relationships-familybox"
              style={{
                stroke: color,
                fill: `${color}22`,
                ...(isRole ? { strokeDasharray: '6 4' } : {})
              }}
              rx={8}
            />
            {(isRole || box.label) && (
              <text
                x={box.x + 8}
                y={box.y + 16}
                className="relationships-familylabel"
                style={{ fill: color }}
              >
                {isRole ? box.label : t('relationships.familyPrefix', { name: box.label })}
              </text>
            )}
          </g>
        )
      })}
      {layout.edges.map((edge, i) => (
        <g key={i}>
          <line
            x1={edge.x1}
            y1={edge.y1}
            x2={edge.x2}
            y2={edge.y2}
            className="relationships-edge"
            data-category={edge.category || undefined}
          />
          {edge.label && (
            <text x={edge.labelX} y={edge.labelY} className="relationships-edgelabel">
              {edge.label}
            </text>
          )}
        </g>
      ))}
      {layout.nodes.map((node) => (
        <g
          key={node.id}
          className="relationships-node-group"
          role="button"
          tabIndex={0}
          // Click follows the thread without leaving the view; the
          // article is a deliberate second gesture, because opening one
          // loses the shape you were reading.
          onClick={(e) => (e.altKey ? openEntity(node.id) : recentre(node.id))}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault()
              if (e.altKey) openEntity(node.id)
              else recentre(node.id)
            }
          }}
        >
          <title>{t('relationships.recentreOn', { name: node.name })}</title>
          {/* Shape and colour together, because five classes on one
            canvas are unreadable by either alone: a place is square, a
            thing is a pill, a scene is a cut corner, people stay the
            rounded box the graph has always drawn. */}
          <rect
            x={node.x}
            y={node.y}
            width={NODE_SIZE.width}
            height={NODE_SIZE.height}
            className={`relationships-node type-${node.entityType}`}
            rx={NODE_RADIUS[node.entityType] ?? 6}
          />
          <text
            x={node.x + NODE_SIZE.width / 2}
            y={node.y + NODE_SIZE.height / 2 + 4}
            className="relationships-nodelabel"
          >
            {fitLabel(node.name)}
          </text>
          {/* What this person is to the one the graph is centred on.
            Under the name rather than in a tooltip: the whole reason to
            centre on somebody is to read this off every node at once. */}
          {kinship[node.id] && (
            <text
              x={node.x + NODE_SIZE.width / 2}
              y={node.y + NODE_SIZE.height + 14}
              className="relationships-kinship"
            >
              {kinship[node.id]}
            </text>
          )}
        </g>
      ))}
    </svg>
  )
}
