import type { TFunction } from 'i18next'

/**
 * What is being exported, kept apart from what file it comes out as.
 *
 * These were one dropdown, which meant "Codex (Markdown)" sat among the file
 * formats as if it were one - two different questions answered by one control.
 */
export const CONTENTS = [
  { key: 'manuscript', labelKey: 'export.contentManuscript' },
  { key: 'codex', labelKey: 'export.contentCodex' },
  // Everything Novalist writes is prose or a document. Nothing machine-readable
  // left the project, so an outline could only reach a spreadsheet by retyping.
  { key: 'data', labelKey: 'export.contentData' },
  // Compiled out of what the writer already recorded. Every scene carried a
  // synopsis and a POV, and neither could be read as a whole.
  { key: 'report', labelKey: 'export.contentReport' }
] as const
export type Content = (typeof CONTENTS)[number]['key']

export const FORMATS: { format: string; extension: string; labelKey: string; content: Content }[] = [
  { format: 'Epub', extension: '.epub', labelKey: 'export.formatEpub', content: 'manuscript' },
  { format: 'Docx', extension: '.docx', labelKey: 'export.formatDocx', content: 'manuscript' },
  { format: 'Pdf', extension: '.pdf', labelKey: 'export.formatPdf', content: 'manuscript' },
  {
    format: 'Markdown',
    extension: '.md',
    labelKey: 'export.formatMarkdown',
    content: 'manuscript'
  },
  {
    format: 'FinalDraft',
    extension: '.fdx',
    labelKey: 'export.formatFinalDraft',
    content: 'manuscript'
  },
  { format: 'LaTeX', extension: '.tex', labelKey: 'export.formatLatex', content: 'manuscript' },
  // An edition of the book like any other, compiled from the same selection -
  // it simply takes hours and comes out as sound, so it runs its own panel
  // rather than the one-shot Export button.
  {
    format: 'Audiobook',
    extension: '.m4b',
    labelKey: 'export.formatAudiobook',
    content: 'manuscript'
  },
  { format: 'Codex', extension: '.md', labelKey: 'export.formatMarkdown', content: 'codex' },
  { format: 'CodexPdf', extension: '.pdf', labelKey: 'export.formatPdf', content: 'codex' },
  { format: 'Csv', extension: '.csv', labelKey: 'export.formatCsv', content: 'data' },
  { format: 'Json', extension: '.json', labelKey: 'export.formatJson', content: 'data' },
  { format: 'CodexCsv', extension: '.csv', labelKey: 'export.formatCodexCsv', content: 'data' },
  { format: 'Opml', extension: '.opml', labelKey: 'export.formatOpml', content: 'data' },
  { format: 'WorldJson', extension: '.json', labelKey: 'export.formatWorldJson', content: 'data' },
  { format: 'WorldHtml', extension: '.html', labelKey: 'export.formatWorldHtml', content: 'data' },
  {
    format: 'SynopsisReport',
    extension: '.md',
    labelKey: 'export.formatSynopsisReport',
    content: 'report'
  },
  { format: 'PovReport', extension: '.md', labelKey: 'export.formatPovReport', content: 'report' }
]

/** Codex entity kinds, in the order the export renders them. */
export const ENTITY_KINDS: { kind: string; labelKey: string }[] = [
  { kind: 'character', labelKey: 'codexHub.characters' },
  { kind: 'location', labelKey: 'codexHub.locations' },
  { kind: 'item', labelKey: 'codexHub.items' },
  { kind: 'lore', labelKey: 'codexHub.lore' }
]

export interface EntityOption {
  key: string
  name: string
}

/** Whatever a picker needs; the layout editor reads the rest of the record. */
export interface PresetDto {
  id: string
  displayName: string
  description: string
  isCustom: boolean
}

/** What the current selection would produce, from the same compile the export runs. */
export interface PreviewDto {
  chapters: number
  scenes: number
  words: number
  characters: number
  pages: number
  /** Exact only on the Normseite grid; an estimate everywhere else. */
  pagesAreExact: boolean
  undescribedImages: number
}

export interface ExtensionFormatDto {
  formatKey: string
  displayName: string
  fileExtension: string
  supportsCover: boolean
}

// Fixed Codex labels are translated by the renderer before crossing the RPC boundary.
export const codexLabels = (t: TFunction): Record<string, string> => ({
  characters: t('codexHub.characters'),
  locations: t('codexHub.locations'),
  items: t('codexHub.items'),
  lore: t('codexHub.lore'),
  relationships: t('export.codexLabel.relationships'),
  role: t('export.codexLabel.role'),
  age: t('export.codexLabel.age'),
  gender: t('export.codexLabel.gender'),
  group: t('export.codexLabel.group'),
  eyes: t('export.codexLabel.eyes'),
  hair: t('export.codexLabel.hair'),
  height: t('export.codexLabel.height'),
  build: t('export.codexLabel.build'),
  skin: t('export.codexLabel.skin'),
  notable: t('export.codexLabel.notable'),
  type: t('export.codexLabel.type'),
  description: t('export.codexLabel.description')
})
