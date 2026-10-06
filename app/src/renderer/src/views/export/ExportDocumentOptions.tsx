import { useTranslation } from 'react-i18next'
import type { ExportModel } from './useExportModel'

export function ExportDocumentOptions({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    format, tocDepth, setTocDepth, tocTitle, setTocTitle, referenceDoc, setReferenceDoc
  } = model
  return (
    <>
      {/* How deep the contents list goes, and what it is called. A flat
        chapter list is right for a novel and wrong for a collection, and
        "Table of Contents" is wrong in every language but English. */}
      {format === 'Epub' && (
        <div className="export-field-row">
          <label className="export-field">
            <span className="export-field-label">{t('export.tocDepth')}</span>
            <select
              className="inspector-input"
              value={tocDepth}
              onChange={(e) => setTocDepth(Number(e.target.value))}
            >
              <option value={1}>{t('export.tocDepthChapters')}</option>
              <option value={2}>{t('export.tocDepthScenes')}</option>
            </select>
          </label>
          <label className="export-field">
            <span className="export-field-label">{t('export.tocTitle')}</span>
            <input
              className="inspector-input"
              value={tocTitle}
              placeholder={t('export.tocTitlePlaceholder')}
              onChange={(e) => setTocTitle(e.target.value)}
            />
          </label>
        </div>
      )}

      {/* A house style arrives as a styled Word file, not as a list of
        settings. Point at it once and the export comes out in it. */}
      {format === 'Docx' && (
        <div className="export-field-row">
          <label className="export-field export-field-grow">
            <span className="export-field-label">{t('export.referenceDoc')}</span>
            <input
              className="inspector-input"
              value={referenceDoc}
              placeholder={t('export.referenceDocPlaceholder')}
              onChange={(e) => setReferenceDoc(e.target.value)}
            />
          </label>
          <button
            type="button"
            className="btn-secondary"
            onClick={() => {
              void window.novalist
                .pickFile(t('export.referenceDoc'), 'all')
                .then((chosen) => chosen && setReferenceDoc(chosen))
            }}
          >
            {t('export.referenceDocChoose')}
          </button>
          {referenceDoc && (
            <button type="button" className="btn-secondary" onClick={() => setReferenceDoc('')}>
              {t('export.referenceDocClear')}
            </button>
          )}
        </div>
      )}
    </>
  )
}

export function ExportCodexOptions({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    isCodex, codexParts, setCodexParts, sectionTitles, pickedSections, setPickedSections
  } = model
  return (
    <>
      {/* Which parts of an entry leave the project. A series bible that has
        to leave the portraits out, or a packet that wants the names and
        nothing else, was an all-or-nothing choice per entry before this. */}
      {isCodex && (
        <div className="export-field-row">
          {(['images', 'fields', 'relationships', 'sections'] as const).map((part) => (
            <label key={part} className="relationships-toggle export-toggle">
              <input
                type="checkbox"
                checked={codexParts.has(part)}
                onChange={(e) =>
                  setCodexParts((prev) => {
                    const next = new Set(prev)
                    if (e.target.checked) next.add(part)
                    else next.delete(part)
                    return next
                  })
                }
              />
              {t(`export.codexPart_${part}`)}
            </label>
          ))}
        </div>
      )}
      {isCodex && codexParts.has('sections') && sectionTitles.length > 0 && (
        <div className="export-field-row">
          <span className="export-field-label">{t('export.codexSections')}</span>
          {sectionTitles.map((title) => (
            <label key={title} className="relationships-toggle export-toggle">
              <input
                type="checkbox"
                checked={pickedSections.has(title)}
                onChange={(e) =>
                  setPickedSections((prev) => {
                    const next = new Set(prev)
                    if (e.target.checked) next.add(title)
                    else next.delete(title)
                    return next
                  })
                }
              />
              {title}
            </label>
          ))}
        </div>
      )}
    </>
  )
}

export function ExportRetailerOptions({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const { retailers, isCodex, isData, retailerKey, setRetailerKey } = model
  return (
    <>
      {/* A build for one shop. Back matter written with <$storename> and
        <$storelink> resolves to that shop, so a reader is sent back where
        they bought it rather than to a competitor. */}
      {retailers.length > 0 && !isCodex && !isData && (
        <label className="export-field">
          <span className="export-field-label">{t('export.buildFor')}</span>
          <select
            className="inspector-input"
            value={retailerKey}
            onChange={(e) => setRetailerKey(e.target.value)}
          >
            <option value="">{t('export.buildForNone')}</option>
            {retailers.map((r) => (
              <option key={r.key} value={r.key}>
                {r.name || r.key}
              </option>
            ))}
          </select>
        </label>
      )}
    </>
  )
}
