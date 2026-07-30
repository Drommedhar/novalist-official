import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface ExportLayoutPanelProps {
  /** The layout picked in the export form; this panel edits that one. */
  selectedId: string
  /** Hands the refreshed list back, and the id to select when it changed. */
  onLayouts: (layouts: ExportLayout[], select?: string) => void
}

/** Trim, margins, gutter and bleed, in inches. */
interface PrintSpec {
  trimWidthInches: number
  trimHeightInches: number
  marginInsideInches: number
  marginOutsideInches: number
  marginTopInches: number
  marginBottomInches: number
  mirrorMargins: boolean
  gutterInches: number
  gutterFromPageCount: boolean
  bleedInches: number
  avoidWidowsAndOrphans: boolean
  minLinesTogether: number
}

interface Trim {
  name: string
  widthInches: number
  heightInches: number
}

/** The manuscript page: US Letter, one margin all round, no bleed. */
const MANUSCRIPT_PAGE: PrintSpec = {
  trimWidthInches: 8.5,
  trimHeightInches: 11,
  marginInsideInches: 1,
  marginOutsideInches: 1,
  marginTopInches: 1,
  marginBottomInches: 1,
  mirrorMargins: true,
  gutterInches: 0,
  gutterFromPageCount: true,
  bleedInches: 0,
  avoidWidowsAndOrphans: true,
  minLinesTogether: 2
}

interface ExportLayout {
  id: string
  displayName: string
  description: string
  /** False for a built-in: it can be copied but not edited. */
  isCustom: boolean
  bodyFontFamily: string
  bodyFontSizePt: number
  lineSpacingMultiplier: number
  marginInches: number
  firstLineIndentInches: number
  chapterTopMarginInches: number
  sceneSeparator: string
  runningHead: string
  doubleSpaced: boolean
  showSceneTitles: boolean
  chapterTitleFormat: string
  chapterNumberStyle: string
  chapterHeadingUppercase: boolean
  dropCap: boolean
  leadInSmallCapsWords: number
  ebookCss: string
  /** The page as a print shop describes it; null keeps the manuscript page. */
  print: PrintSpec | null
}

/** The numerals a chapter heading can write its number in. */
const NUMBER_STYLES = ['Arabic', 'RomanUpper', 'RomanLower', 'Words'] as const

/**
 * Authoring an export layout.
 *
 * Novalist shipped four fixed presets and no way to change any of them, so a
 * writer with a submission guideline asking for something slightly different
 * had nowhere to put it. Built-ins stay read-only and are copied instead: a
 * preset named after a standard that no longer matches it is worse than no
 * preset at all, and nothing would tell them.
 *
 * Which layout is edited follows the export form's own dropdown. A second
 * picker here listed the same layouts and let the two disagree about which one
 * was about to be used.
 */
export function ExportLayoutPanel({
  selectedId,
  onLayouts
}: ExportLayoutPanelProps): React.JSX.Element {
  const { t } = useTranslation()
  const [layouts, setLayouts] = useState<ExportLayout[]>([])

  const [trims, setTrims] = useState<Trim[]>([])

  useEffect(() => {
    void rpc.request<ExportLayout[]>('exportPresets/list').then(setLayouts)
    void rpc.request<Trim[]>('exportPresets/trims').then(setTrims).catch(() => setTrims([]))
  }, [])

  const selected = layouts.find((l) => l.id === selectedId)

  const edit = (patch: Partial<ExportLayout>): void => {
    if (!selected) return
    setLayouts(layouts.map((l) => (l.id === selected.id ? { ...l, ...patch } : l)))
  }

  /** Patches the print spec, which is nested and may not exist yet. */
  const editPrint = (patch: Partial<PrintSpec>): void => {
    if (!selected?.print) return
    edit({ print: { ...selected.print, ...patch } })
  }

  const publish = (all: ExportLayout[], select?: string): void => {
    setLayouts(all)
    onLayouts(all, select)
  }

  const persist = async (layout: ExportLayout): Promise<void> => {
    if (!layout.isCustom) return
    publish(await rpc.request<ExportLayout[]>('exportPresets/save', [layout]))
  }

  /**
   * Saves what is on screen. A control that changes and saves in one gesture -
   * a checkbox, a dropdown - has to hand over the value it just produced: the
   * state update has not landed yet, so saving from state would write back the
   * value the writer just changed away from and snap the control back.
   */
  const save = async (patch?: Partial<ExportLayout>): Promise<void> => {
    if (!selected) return
    await persist(patch ? { ...selected, ...patch } : selected)
  }

  // A copy is what the writer meant to work on, so the export switches to it.
  const duplicate = async (): Promise<void> => {
    if (!selected) return
    const all = await rpc.request<ExportLayout[]>('exportPresets/duplicate', [
      selected.id,
      `${selected.displayName} (copy)`
    ])
    publish(all, all[all.length - 1]?.id)
  }

  const remove = async (): Promise<void> => {
    if (!selected?.isCustom) return
    const all = await rpc.request<ExportLayout[]>('exportPresets/delete', [selected.id])
    publish(all, all[0]?.id ?? '')
  }

  const number = (
    key: keyof ExportLayout,
    step = 0.1
  ): React.JSX.Element => (
    <input
      className="inspector-input"
      type="number"
      step={step}
      disabled={!selected?.isCustom}
      value={Number(selected?.[key] ?? 0)}
      onChange={(e) => edit({ [key]: Number(e.target.value) } as Partial<ExportLayout>)}
      onBlur={() => void save()}
    />
  )

  return (
    <div className="settings-subgroup">
      <div className="settings-button-row">
        <span className="settings-hint">{selected?.displayName ?? ''}</span>
        <button className="dialog-button" onClick={() => void duplicate()}>
          <Copy size={14} /> {t('layout.duplicate')}
        </button>
        {selected?.isCustom && (
          <button className="dialog-button danger" onClick={() => void remove()}>
            <Trash2 size={14} />
          </button>
        )}
      </div>

      {selected && !selected.isCustom && (
        <div className="match-hint">{t('layout.builtInHint')}</div>
      )}

      {selected && (
        <>
          <label className="inspector-label">{t('layout.name')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.displayName}
            onChange={(e) => edit({ displayName: e.target.value })}
            onBlur={() => void save()}
          />

          <label className="inspector-label">{t('layout.bodyFont')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.bodyFontFamily}
            onChange={(e) => edit({ bodyFontFamily: e.target.value })}
            onBlur={() => void save()}
          />

          <label className="inspector-label">{t('layout.fontSize')}</label>
          {number('bodyFontSizePt', 0.5)}

          <label className="inspector-label">{t('layout.lineSpacing')}</label>
          {number('lineSpacingMultiplier')}

          <label className="inspector-label">{t('layout.margin')}</label>
          {number('marginInches')}

          <label className="inspector-label">{t('layout.indent')}</label>
          {number('firstLineIndentInches')}

          <label className="inspector-label">{t('layout.chapterTopMargin')}</label>
          {number('chapterTopMarginInches')}

          <label className="inspector-label">{t('layout.separator')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.sceneSeparator}
            onChange={(e) => edit({ sceneSeparator: e.target.value })}
            onBlur={() => void save()}
          />

          {/* The line at the top of every page. Empty keeps the submission
              default rather than printing nothing, because a manuscript with
              no running head is one a reader cannot reassemble if it is
              dropped. */}
          <label className="inspector-label">{t('layout.runningHead')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            placeholder={t('layout.runningHeadPlaceholder')}
            value={selected.runningHead}
            onChange={(e) => edit({ runningHead: e.target.value })}
            onBlur={() => void save()}
          />

          <label className="inspector-label">{t('layout.chapterTitleFormat')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.chapterTitleFormat}
            onChange={(e) => edit({ chapterTitleFormat: e.target.value })}
            onBlur={() => void save()}
          />
          <div className="match-hint">{t('layout.chapterTitleFormatHint')}</div>

          <label className="inspector-label">{t('layout.chapterNumberStyle')}</label>
          <select
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.chapterNumberStyle}
            onChange={(e) => {
              edit({ chapterNumberStyle: e.target.value })
              void save()
            }}
          >
            {NUMBER_STYLES.map((style) => (
              <option key={style} value={style}>
                {t(`layout.numberStyle${style}`)}
              </option>
            ))}
          </select>

          <label className="match-toggle">
            <input
              type="checkbox"
              disabled={!selected.isCustom}
              checked={selected.chapterHeadingUppercase}
              onChange={(e) => {
                edit({ chapterHeadingUppercase: e.target.checked })
                void save()
              }}
            />
            {t('layout.chapterHeadingUppercase')}
          </label>

          <label className="match-toggle">
            <input
              type="checkbox"
              disabled={!selected.isCustom}
              checked={selected.dropCap}
              onChange={(e) => {
                edit({ dropCap: e.target.checked })
                void save()
              }}
            />
            {t('layout.dropCap')}
          </label>

          {selected.dropCap && (
            <>
              <label className="inspector-label">{t('layout.leadInSmallCaps')}</label>
              <input
                className="inspector-input"
                type="number"
                min={0}
                max={12}
                disabled={!selected.isCustom}
                value={selected.leadInSmallCapsWords}
                onChange={(e) => edit({ leadInSmallCapsWords: Number(e.target.value) })}
                onBlur={() => void save()}
              />
              <div className="match-hint">{t('layout.leadInSmallCapsHint')}</div>
            </>
          )}

          <label className="match-toggle">
            <input
              type="checkbox"
              disabled={!selected.isCustom}
              checked={selected.showSceneTitles}
              onChange={(e) => {
                edit({ showSceneTitles: e.target.checked })
                void save({ showSceneTitles: e.target.checked })
              }}
            />
            {t('layout.showSceneTitles')}
          </label>
          <div className="match-hint">{t('layout.showSceneTitlesHint')}</div>

          <label className="inspector-label">{t('layout.ebookCss')}</label>
          <textarea
            className="inspector-input publishing-textarea"
            disabled={!selected.isCustom}
            value={selected.ebookCss}
            onChange={(e) => edit({ ebookCss: e.target.value })}
            onBlur={() => void save()}
          />
          <div className="match-hint">{t('layout.ebookCssHint')}</div>

          {/* A manuscript is one page size with one margin all round because
              it is read on a screen. A book is not, and a file that gets it
              wrong comes back from the printer. */}
          <label className="match-toggle">
            <input
              type="checkbox"
              disabled={!selected.isCustom}
              checked={selected.print !== null}
              onChange={(e) => {
                const next = e.target.checked ? MANUSCRIPT_PAGE : null
                edit({ print: next })
                void save({ print: next })
              }}
            />
            {t('layout.printSpec')}
          </label>
          <div className="match-hint">{t('layout.printSpecHint')}</div>

          {selected.print && (
            <>
              <label className="inspector-label">{t('layout.trim')}</label>
              <select
                className="inspector-input"
                disabled={!selected.isCustom}
                value={
                  trims.find(
                    (trim) =>
                      trim.widthInches === selected.print?.trimWidthInches &&
                      trim.heightInches === selected.print?.trimHeightInches
                  )?.name ?? ''
                }
                onChange={(e) => {
                  const trim = trims.find((candidate) => candidate.name === e.target.value)
                  if (!trim || !selected.print) return
                  const sized = {
                    ...selected.print,
                    trimWidthInches: trim.widthInches,
                    trimHeightInches: trim.heightInches
                  }
                  edit({ print: sized })
                  void save({ print: sized })
                }}
              >
                <option value="">{t('layout.trimCustom')}</option>
                {trims.map((trim) => (
                  <option key={trim.name} value={trim.name}>
                    {t(`layout.trim_${trim.name.replace(/-/g, '_')}`, {
                      defaultValue: trim.name
                    })}{' '}
                    ({trim.widthInches} x {trim.heightInches})
                  </option>
                ))}
              </select>

              <div className="layout-print-grid">
                {(
                  [
                    ['marginInsideInches', 'layout.marginInside'],
                    ['marginOutsideInches', 'layout.marginOutside'],
                    ['marginTopInches', 'layout.marginTop'],
                    ['marginBottomInches', 'layout.marginBottom'],
                    ['bleedInches', 'layout.bleed']
                  ] as const
                ).map(([key, label]) => (
                  <label key={key} className="layout-print-field">
                    <span className="inspector-label">{t(label)}</span>
                    <input
                      className="inspector-input"
                      type="number"
                      step={0.05}
                      min={0}
                      disabled={!selected.isCustom}
                      value={selected.print?.[key] ?? 0}
                      onChange={(e) => editPrint({ [key]: Number(e.target.value) })}
                      onBlur={() => void save()}
                    />
                  </label>
                ))}
              </div>

              <label className="match-toggle">
                <input
                  type="checkbox"
                  disabled={!selected.isCustom}
                  checked={selected.print.mirrorMargins}
                  onChange={(e) => {
                    editPrint({ mirrorMargins: e.target.checked })
                    void save({ print: { ...selected.print!, mirrorMargins: e.target.checked } })
                  }}
                />
                {t('layout.mirrorMargins')}
              </label>
              <div className="match-hint">{t('layout.mirrorMarginsHint')}</div>

              <label className="match-toggle">
                <input
                  type="checkbox"
                  disabled={!selected.isCustom}
                  checked={selected.print.gutterFromPageCount}
                  onChange={(e) => {
                    editPrint({ gutterFromPageCount: e.target.checked })
                    void save({ print: { ...selected.print!, gutterFromPageCount: e.target.checked } })
                  }}
                />
                {t('layout.gutterAuto')}
              </label>
              <div className="match-hint">{t('layout.gutterAutoHint')}</div>

              {!selected.print.gutterFromPageCount && (
                <>
                  <label className="inspector-label">{t('layout.gutter')}</label>
                  <input
                    className="inspector-input"
                    type="number"
                    step={0.05}
                    min={0}
                    disabled={!selected.isCustom}
                    value={selected.print.gutterInches}
                    onChange={(e) => editPrint({ gutterInches: Number(e.target.value) })}
                    onBlur={() => void save()}
                  />
                </>
              )}

              <label className="match-toggle">
                <input
                  type="checkbox"
                  disabled={!selected.isCustom}
                  checked={selected.print.avoidWidowsAndOrphans}
                  onChange={(e) => {
                    editPrint({ avoidWidowsAndOrphans: e.target.checked })
                    void save({ print: { ...selected.print!, avoidWidowsAndOrphans: e.target.checked } })
                  }}
                />
                {t('layout.widows')}
              </label>
              <div className="match-hint">{t('layout.widowsHint')}</div>
            </>
          )}
        </>
      )}
    </div>
  )
}
