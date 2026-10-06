import { useTranslation } from 'react-i18next'
import type { ExportModel } from './useExportModel'

export function ExportChapterSelection({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    chaptersVisible, stages, stageFilter, setStageFilter, otherBooks, extraBooks, setExtraBooks,
    setSelected, chapters, selected, toggle
  } = model
  return (
    <>
      {chaptersVisible && stages.length > 0 && (
        <>
          <div className="inspector-label">{t('export.stageFilter')}</div>
          <div className="export-stage-filter">
            <label className="relationships-toggle">
              <input
                type="checkbox"
                checked={stageFilter.size === 0}
                onChange={() => setStageFilter(new Set())}
              />
              {t('export.stageFilterAll')}
            </label>
            {stages.map((stage) => (
              <label key={stage.key} className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={stageFilter.has(stage.key)}
                  onChange={(e) => {
                    const next = new Set(stageFilter)
                    if (e.target.checked) next.add(stage.key)
                    else next.delete(stage.key)
                    setStageFilter(next)
                  }}
                />
                {stage.label}
              </label>
            ))}
          </div>
          <div className="settings-hint">{t('export.excludedNote')}</div>
        </>
      )}

      {/* A series in one file. The chapter list belongs to the open book, so
        a further volume comes in whole rather than chapter by chapter. */}
      {chaptersVisible && otherBooks.length > 0 && (
        <>
          <div className="export-chapters-header">
            <span className="export-field-label">{t('export.alsoInclude')}</span>
          </div>
          <div className="export-stage-filter">
            {otherBooks.map((book) => (
              <label key={book.id} className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={extraBooks.has(book.id)}
                  onChange={(e) => {
                    const next = new Set(extraBooks)
                    if (e.target.checked) next.add(book.id)
                    else next.delete(book.id)
                    setExtraBooks(next)
                  }}
                />
                {book.name}
              </label>
            ))}
          </div>
        </>
      )}

      {chaptersVisible && (
        <>
          <div className="export-chapters-header">
            <div className="inspector-label">{t('export.selectChapters')}</div>
            <div className="export-select-buttons">
              <button
                className="export-inline-btn"
                onClick={() => setSelected(new Set(chapters.map((c) => c.guid)))}
              >
                {t('export.selectAll')}
              </button>
              <button className="export-inline-btn" onClick={() => setSelected(new Set())}>
                {t('export.selectNone')}
              </button>
            </div>
          </div>
          <div className="export-chapters">
            {chapters.map((chapter) => (
              <label key={chapter.guid} className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={selected.has(chapter.guid)}
                  onChange={(e) => toggle(chapter.guid, e.target.checked)}
                />
                {chapter.title}
              </label>
            ))}
          </div>
          <span className="export-count">
            {t('export.selectedOfTotal', { selected: selected.size, total: chapters.length })}
          </span>
        </>
      )}
    </>
  )
}

export function ExportEntitySelection({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    entitiesVisible, allEntities, setSelectedEntities, visibleKeys, entityQuery, setEntityQuery,
    visibleEntities, selectedEntities, toggleEntity
  } = model
  return (
    <>
      {entitiesVisible && allEntities.length > 0 && (
        <>
          <div className="export-chapters-header">
            <div className="inspector-label">{t('export.selectEntities')}</div>
            <div className="export-select-buttons">
              <button
                className="export-inline-btn"
                onClick={() =>
                  setSelectedEntities((prev) => new Set([...prev, ...visibleKeys]))
                }
              >
                {t('export.selectAll')}
              </button>
              <button
                className="export-inline-btn"
                onClick={() =>
                  setSelectedEntities((prev) => {
                    const next = new Set(prev)
                    for (const key of visibleKeys) next.delete(key)
                    return next
                  })
                }
              >
                {t('export.selectNone')}
              </button>
            </div>
          </div>
          <input
            className="dialog-input export-entity-search"
            type="search"
            value={entityQuery}
            placeholder={t('export.searchEntities')}
            onChange={(e) => setEntityQuery(e.target.value)}
          />
          <div className="export-chapters">
            {visibleEntities.map(({ kind, labelKey, list }) => (
              <div key={kind} className="export-entity-group">
                <div className="export-entity-group-title">{t(labelKey)}</div>
                {list.map((entity) => (
                  <label key={entity.key} className="relationships-toggle">
                    <input
                      type="checkbox"
                      checked={selectedEntities.has(entity.key)}
                      onChange={(e) => toggleEntity(entity.key, e.target.checked)}
                    />
                    {entity.name}
                  </label>
                ))}
              </div>
            ))}
            {visibleEntities.length === 0 && (
              <span className="export-count">{t('export.noEntityMatches')}</span>
            )}
          </div>
          <span className="export-count">
            {t('export.selectedOfTotal', {
              selected: selectedEntities.size,
              total: allEntities.length
            })}
          </span>
        </>
      )}
    </>
  )
}
