import { useTranslation } from 'react-i18next'
import type { ExportModel } from './useExportModel'
import { CONTENTS, FORMATS, type Content } from './exportTypes'

export function ExportFormatFields({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    content, setContent, format, setFormat, extFormats, isData, presetId, setPresetId, presets,
    activePreset
  } = model
  return (
    <>
      <div className="export-field">
        <label className="inspector-label" htmlFor="export-content">
          {t('export.content')}
        </label>
        <select
          id="export-content"
          className="dialog-input"
          value={content}
          onChange={(e) => setContent(e.target.value as Content)}
        >
          {CONTENTS.map((c) => (
            <option key={c.key} value={c.key}>
              {t(c.labelKey)}
            </option>
          ))}
        </select>
      </div>

      <div className="export-field">
        <label className="inspector-label" htmlFor="export-format">
          {t('export.format')}
        </label>
        <select
          id="export-format"
          className="dialog-input"
          value={format}
          onChange={(e) => setFormat(e.target.value)}
        >
          {FORMATS.filter((f) => f.content === content).map((f) => (
            <option key={f.format} value={f.format}>
              {t(f.labelKey)}
            </option>
          ))}
          {/* An extension writes the manuscript out; nothing contributes a
            codex writer, so those belong under the manuscript only. */}
          {content === 'manuscript' &&
            extFormats.map((f) => (
              <option key={f.formatKey} value={f.formatKey}>
                {f.displayName}
              </option>
            ))}
        </select>
      </div>

      {/* A layout is page geometry and typography; a metadata file has
        neither, so offering one would be a control that changes nothing. */}
      {!isData && (
        <div className="export-field">
          <label className="inspector-label" htmlFor="export-preset">
            {t('export.preset')}
          </label>
          <select
            id="export-preset"
            className="dialog-input"
            value={presetId}
            onChange={(e) => setPresetId(e.target.value)}
          >
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName}
                {p.isCustom ? '' : ` (${t('layout.builtIn')})`}
              </option>
            ))}
          </select>
          {activePreset?.description && (
            <span className="export-preset-desc">{activePreset.description}</span>
          )}
        </div>
      )}
    </>
  )
}

export function ExportTitleFields({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    title, setTitle, author, setAuthor, isData, includeTitlePage, setIncludeTitlePage, format,
    extFormats, includeCover, setIncludeCover, forReaders, setForReaders
  } = model
  return (
    <>
      <div className="export-field">
        <label className="inspector-label" htmlFor="export-title">
          {t('export.title')}
        </label>
        <input
          id="export-title"
          className="dialog-input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
        />
      </div>

      <div className="export-field">
        <label className="inspector-label" htmlFor="export-author">
          {t('export.author')}
        </label>
        <input
          id="export-author"
          className="dialog-input"
          value={author}
          onChange={(e) => setAuthor(e.target.value)}
        />
      </div>

      {!isData && (
        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={includeTitlePage}
            onChange={(e) => setIncludeTitlePage(e.target.checked)}
          />
          {t('export.includeTitlePage')}
        </label>
      )}

      {/* Shown only where a cover actually lands in the file. A control that
        changes nothing is worse than no control, so a contributed format has
        to say it can hold one. */}
      {(format === 'Epub' ||
        format === 'Pdf' ||
        extFormats.some((f) => f.formatKey === format && f.supportsCover)) && (
        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={includeCover}
            onChange={(e) => setIncludeCover(e.target.checked)}
          />
          {t('export.includeCover')}
        </label>
      )}

      {/* Only on the formats that carry the world out of the app. On a
        manuscript export there is nothing for it to hide. */}
      {(format === 'WorldHtml' ||
        format === 'WorldJson' ||
        format === 'Json' ||
        format === 'Codex' ||
        format === 'CodexPdf' ||
        format === 'CodexCsv') && (
        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={forReaders}
            onChange={(e) => setForReaders(e.target.checked)}
          />
          {t('export.forReaders')}
        </label>
      )}
    </>
  )
}
