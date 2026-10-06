




export const ACCESSIBLE_FONTS = [
  'OpenDyslexic',
  'Atkinson Hyperlegible',
  'Lexend',
  'Comic Sans MS',
  'Verdana',
  'Tahoma'
]



export const SYSTEM_FONTS = [
  'Newsreader',
  'Fraunces',
  'Courier Prime',
  'Inter',
  'Times New Roman',
  'Georgia',
  'Garamond',
  'Baskerville',
  'Palatino',
  'Book Antiqua',
  'Cambria',
  'Merriweather',
  'Lora',
  'Arial',
  'Helvetica',
  'Verdana',
  'Calibri',
  'Trebuchet MS',
  'Courier New',
  'Consolas'
]



export const PAGE_FORMATS: { code: string; name: string }[] = [
  { code: 'USTrade6x9', name: 'US Trade (6x9)' },
  { code: 'Digest5_5x8_5', name: 'Digest (5.5x8.5)' },
  { code: 'A5', name: 'A5 (5.83x8.27)' },
  { code: 'MassMarket', name: 'Mass Market (4.25x6.87)' },
  { code: 'Custom', name: 'Custom' }
]



// Mirrors Novalist.Desktop BookWidthCalculator so the live preview matches.
export const PAGE_FORMAT_WIDTHS: Record<string, number> = {
  USTrade6x9: 4.75,
  Digest5_5x8_5: 4.3,
  A5: 4.63,
  MassMarket: 3.35
}


export const MEASURE_SAMPLE =
  'abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ,.;:!?\'-"'



export function estimateCharsPerLine(
  format: string,
  customWidth: number | null,
  fontFamily: string,
  fontSize: number
): number {
  const inches =
    format === 'Custom'
      ? customWidth && customWidth > 0
        ? customWidth
        : 4.75
      : (PAGE_FORMAT_WIDTHS[format] ?? 4.75)
  const px = inches * 96
  const ctx = document.createElement('canvas').getContext('2d')
  if (!ctx) return 65
  ctx.font = `${fontSize}px "${fontFamily}"`
  const avg = ctx.measureText(MEASURE_SAMPLE).width / MEASURE_SAMPLE.length
  return avg > 0 ? Math.round(px / avg) : 65
}
