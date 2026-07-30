import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useCodexStore, type EntitySummary } from '../../stores/codexStore'

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
                  {/* Uncontrolled and committed on blur: a controlled input
                      would round-trip every keystroke through the backend, and
                      the refresh would fight the caret. */}
                  <input
                    className="codex-table-input"
                    defaultValue={column.read(entity)}
                    aria-label={`${entity.name} ${t(column.labelKey)}`}
                    onBlur={(e) => commit(entity, column, e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') e.currentTarget.blur()
                      if (e.key === 'Escape') {
                        e.currentTarget.value = column.read(entity)
                        e.currentTarget.blur()
                      }
                    }}
                  />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
