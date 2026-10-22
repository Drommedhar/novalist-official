import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useCodexStore, type EntitySummary } from '../../stores/codexStore'
import { SharedFieldControl } from './SharedFieldControl'
import { metadataBinding, SharedMetadataField } from '../../collaboration/SharedMetadataField'
import { proseBinding } from '../../collaboration/sharedMarkdown'
import { MarkdownEditor } from '../../shell/MarkdownEditor'

/**
 * One editable column of the table.
 *
 * `field` is the property name the update RPC writes, which is not always what
 * the list calls it: every type reports a "detail", and that detail is a role
 * on a character and a description on everything else.
 */
interface Column {
  key: string
  field: string
  labelKey: string
  /** Reads the current value out of a list row. */
  read: (entity: EntitySummary) => string
}

const NAME: Column = {
  key: 'name',
  field: 'name',
  labelKey: 'codexTable.name',
  read: (e) => e.name
}

const GROUP: Column = {
  key: 'group',
  field: 'group',
  labelKey: 'codexTable.group',
  read: (e) => e.group ?? ''
}

/**
 * The columns for a type. Characters get a role, places get a parent, and the
 * rest get a description - which is the same column under three names, so it is
 * built per type rather than pretending one label fits.
 */
function columnsFor(type: string): Column[] {
  const detail = (field: string, labelKey: string): Column => ({
    key: 'detail',
    field,
    labelKey,
    read: (e) => e.detail ?? ''
  })

  if (type === 'character') {
    return [NAME, detail('role', 'codexTable.role'), GROUP]
  }
  if (type === 'location') {
    return [
      NAME,
      detail('description', 'codexTable.description'),
      { key: 'parent', field: 'parent', labelKey: 'codexTable.parent', read: (e) => e.parent ?? '' },
      GROUP
    ]
  }
  // Items, lore and every custom type: a name, what it is, and its faction.
  return [NAME, detail('description', 'codexTable.description'), GROUP]
}

/**
 * Every entry of a type at once, editable in place.
 *
 * The Codex edited one form at a time, so filing forty characters into their
 * houses meant forty round trips through a detail pane - and comparing two
 * entries meant remembering the first one. A table is the shape that work
 * actually has.
 */
export function EntityTable(): React.JSX.Element {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const entities = useCodexStore((s) => s.entities)
  const columns = columnsFor(entityType)

  const commit = (entity: EntitySummary, column: Column, value: string): void => {
    if (column.read(entity) === value) return
    void rpc
      .request('entities/update', [entityType, entity.id, { [column.field]: value }])
      .then(() => useCodexStore.getState().refresh())
  }

  if (entities.length === 0) {
    return <div className="binder-placeholder">{t('codexTable.empty')}</div>
  }

  return (
    <div className="codex-table-wrap">
      <table className="codex-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key}>{t(column.labelKey)}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {entities.map((entity) => (
            <tr key={entity.id}>
              {columns.map((column) => (
                <td key={column.key}>
                  <TableCell entity={entity} column={column} label={`${entity.name} ${t(column.labelKey)}`}
                    commit={value => commit(entity, column, value)} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function TableCell({ entity, column, label, commit }: {
  entity: EntitySummary; column: Column; label: string; commit(value: string): void
}): React.JSX.Element {
  const shared = entity.sharedFields
  const itemId = shared?.fields[column.field]
  const value = column.read(entity)
  const prose = proseBinding({ sharedProse: entity.sharedProse }, column.field)
  if (prose && !entity.locked) return <MarkdownEditor shared={prose} value={value} onChange={commit} ariaLabel={label} minRows={1} />
  const metadata = shared?.metadata?.[column.field] ? metadataBinding(entity, column.field) : undefined
  if (metadata) return <SharedMetadataField binding={metadata} value={value} onChange={commit}>
    {field => <input className="codex-table-input" aria-label={label} value={field.value} readOnly={!field.editable}
      disabled={entity.locked} onChange={event => field.change(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') event.currentTarget.blur() }} />}
  </SharedMetadataField>
  if (shared && itemId) return <SharedFieldControl binding={{ sessionId: shared.sessionId, itemId }}
    value={value} label={label} disabled={entity.locked} />
  return <input className="codex-table-input" defaultValue={value} aria-label={label}
    readOnly={!!shared} disabled={entity.locked} onBlur={event => commit(event.target.value)}
    onKeyDown={event => {
      if (event.key === 'Enter') event.currentTarget.blur()
      if (event.key === 'Escape') { event.currentTarget.value = value; event.currentTarget.blur() }
    }} />
}
