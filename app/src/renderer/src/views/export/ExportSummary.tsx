import { useTranslation } from 'react-i18next'
import type { ExportModel } from './useExportModel'
import { FORMATS } from './exportTypes'

export function ExportSummary({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    title, projectName, author, extFormats, format, preview, chaptersVisible, chapters, selected,
    visibleEntities, selectedEntities
  } = model
  return (
    <aside className="export-summary">
      <h2>{t('desktopRefresh.exportSummary')}</h2>
      <div className="export-title-preview">
        <h3>{title || projectName}</h3>
        <p>{author}</p>
        <small>
          {extFormats.find((f) => f.formatKey === format)?.displayName ??
            t(FORMATS.find((f) => f.format === format)?.labelKey ?? 'export.format')}
        </small>
      </div>
      {preview && (
        <p className="export-preview" aria-live="polite">
          {t('export.previewCounts', {
            chapters: preview.chapters,
            scenes: preview.scenes,
            words: preview.words.toLocaleString()
          })}{' '}
          {t(preview.pagesAreExact ? 'export.previewPagesExact' : 'export.previewPages', {
            pages: preview.pages
          })}
          {preview.undescribedImages > 0 && (
            <>
              {' '}
              <span className="export-warning">
                {t('export.previewUndescribed', { count: preview.undescribedImages })}
              </span>
            </>
          )}
        </p>
      )}
      <div className="export-summary-contents">
        {chaptersVisible
          ? chapters
              .filter((c) => selected.has(c.guid))
              .map((c) => <div key={c.guid}>{c.title}</div>)
          : visibleEntities
              .flatMap((g) => g.list)
              .filter((e) => selectedEntities.has(e.key))
              .map((e) => <div key={e.key}>{e.name}</div>)}
      </div>
    </aside>
  )
}
