import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

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
  doubleSpaced: boolean
  showSceneTitles: boolean
  chapterTitleFormat: string
  ebookCss: string
}

/**
 * Authoring an export layout.
 *
 * Novalist shipped four fixed presets and no way to change any of them, so a
 * writer with a submission guideline asking for something slightly different
 * had nowhere to put it. Built-ins stay read-only and are copied instead: a
 * preset named after a standard that no longer matches it is worse than no
 * preset at all, and nothing would tell them.
 */
export function ExportLayoutPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [layouts, setLayouts] = useState<ExportLayout[]>([])
  const [selectedId, setSelectedId] = useState('')

  useEffect(() => {
    void rpc.request<ExportLayout[]>('exportPresets/list').then((all) => {
      setLayouts(all)
      setSelectedId((current) => current || all[0]?.id || '')
    })
  }, [])

  const selected = layouts.find((l) => l.id === selectedId)

  const edit = (patch: Partial<ExportLayout>): void => {
    if (!selected) return
    setLayouts(layouts.map((l) => (l.id === selected.id ? { ...l, ...patch } : l)))
  }

  const save = async (): Promise<void> => {
    if (!selected?.isCustom) return
    setLayouts(await rpc.request<ExportLayout[]>('exportPresets/save', [selected]))
  }

  const duplicate = async (): Promise<void> => {
    if (!selected) return
    const all = await rpc.request<ExportLayout[]>('exportPresets/duplicate', [
      selected.id,
      `${selected.displayName} (copy)`
    ])
    setLayouts(all)
    setSelectedId(all[all.length - 1]?.id ?? selectedId)
  }

  const remove = async (): Promise<void> => {
    if (!selected?.isCustom) return
    const all = await rpc.request<ExportLayout[]>('exportPresets/delete', [selected.id])
    setLayouts(all)
    setSelectedId(all[0]?.id ?? '')
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
        <select
          className="inspector-input"
          value={selectedId}
          onChange={(e) => setSelectedId(e.target.value)}
        >
          {layouts.map((layout) => (
            <option key={layout.id} value={layout.id}>
              {layout.displayName}
              {layout.isCustom ? '' : ` (${t('layout.builtIn')})`}
            </option>
          ))}
        </select>
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

          <label className="inspector-label">{t('layout.chapterTitleFormat')}</label>
          <input
            className="inspector-input"
            disabled={!selected.isCustom}
            value={selected.chapterTitleFormat}
            onChange={(e) => edit({ chapterTitleFormat: e.target.value })}
            onBlur={() => void save()}
          />
          <div className="match-hint">{t('layout.chapterTitleFormatHint')}</div>

          <label className="match-toggle">
            <input
              type="checkbox"
              disabled={!selected.isCustom}
              checked={selected.showSceneTitles}
              onChange={(e) => {
                edit({ showSceneTitles: e.target.checked })
                void save()
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
        </>
      )}
    </div>
  )
}
