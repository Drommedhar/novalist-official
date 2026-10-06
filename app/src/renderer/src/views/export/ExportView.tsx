import { FileDown } from 'lucide-react'
import { useRef } from 'react'
import { useContentTransition } from '../../shell/useContentTransition'
import { MotionPresence } from '../../shell/MotionPresence'
import { BookMatterPanel } from './BookMatterPanel'
import { PublishingPanel } from './PublishingPanel'
import { ReplacementsPanel } from './ReplacementsPanel'
import { ExportLayoutPanel } from './ExportLayoutPanel'
import { AudiobookPanel } from './AudiobookPanel'
import { useTranslation } from 'react-i18next'
import { ReviewImportDialog } from '../../shell/ReviewImportDialog'
import { useExportModel, type ExportModel } from './useExportModel'
import { ExportFormatFields, ExportTitleFields } from './ExportFields'
import { ExportDocumentOptions, ExportCodexOptions, ExportRetailerOptions } from './ExportDocumentOptions'
import { ExportChapterSelection, ExportEntitySelection } from './ExportSelection'
import { ExportSummary } from './ExportSummary'
import './export.css'
import './mobile-export.css'

export function ExportView(): React.JSX.Element {
  const { t } = useTranslation()
  const model = useExportModel()
  const contentRef = useRef<HTMLDivElement>(null)
  useContentTransition(contentRef, `${model.content}:${model.format}`)
  return (
    <div className="dashboard export-view">
      <h1 className="dashboard-title">{t('shell.view.export')}</h1>
      {window.novalist.isMobile && (
        <p className="export-mobile-hint">{t('export.mobileShareHint')}</p>
      )}
      <div className="export-workspace" ref={contentRef}>
        <div className="dashboard-card export-card">
          <ExportFormatFields model={model} />
          <ExportTitleFields model={model} />
          <ExportDocumentOptions model={model} />
          <ExportCodexOptions model={model} />
          <ExportRetailerOptions model={model} />
          <ExportChapterSelection model={model} />
          <ExportEntitySelection model={model} />
          <ExportDetails model={model} />
        </div>
        <ExportSummary model={model} />
      </div>
      <MotionPresence>{model.reviewOpen && <ReviewImportDialog onClose={() => model.setReviewOpen(false)} />}</MotionPresence>
    </div>
  )
}

function ExportDetails({ model }: { model: ExportModel }): React.JSX.Element {
  const { t } = useTranslation()
  const {
    isData, presetId, setPresets, setPresetId, setReviewOpen, isAudiobook, selected, title,
    exportDisabled, run, busy, result
  } = model
  return (
    <>
      {/* The pages around the story. Typed, so each is set its own way. */}
      {!isData && (
        <details className="export-matter">
          <summary>{t('matter.title')}</summary>
          <BookMatterPanel />
        </details>
      )}

      {/* What a shop and a distributor need, beyond title and author. */}
      {!isData && (
        <details className="export-matter">
          <summary>{t('publishing.title')}</summary>
          <PublishingPanel />
          {/* Applied to the output only, so a rule can be turned off without
          anything to undo - unlike Find and Replace, which rewrites the
          scenes themselves. */}
          <ReplacementsPanel />
        </details>
      )}

      {/* Page geometry, separators and ebook CSS for whichever layout is
        picked above, rather than a second dropdown listing the same ones. */}
      {!isData && (
        <details className="export-matter">
          <summary>{t('layout.title')}</summary>
          <ExportLayoutPanel
            selectedId={presetId}
            onLayouts={(all, select) => {
              setPresets(all)
              if (select !== undefined) setPresetId(select)
            }}
          />
        </details>
      )}

      {/* The other half of the round trip: a DOCX goes out to an editor and
        their marked-up copy comes back here. */}
      <div className="settings-button-row export-review-row">
        <button className="dialog-button" onClick={() => setReviewOpen(true)}>
          {t('review.openAction')}
        </button>
        <span className="settings-hint">{t('review.openHint')}</span>
      </div>

      {isAudiobook ? (
        <AudiobookPanel selectedChapterGuids={[...selected]} title={title} />
      ) : (
        <div className="export-action-bar">
          <button
            className="start-open export-run"
            disabled={exportDisabled}
            onClick={() => void run()}
          >
            <FileDown size={15} strokeWidth={2} />
            {busy ? t('export.exporting') : t('export.exportAction')}
          </button>
          {result && <p className="inspector-meta export-result">{result}</p>}
        </div>
      )}
    </>
  )
}
