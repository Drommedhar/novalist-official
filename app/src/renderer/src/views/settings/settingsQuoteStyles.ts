


/** Opening/closing quote pair per auto-replacement language, for the preview. */
export const QUOTE_PREVIEW: Record<string, [string, string]> = {
  en: ['“', '”'],
  'de-low': ['„', '“'],
  'de-guillemet': ['»', '«'],
  fr: ['« ', ' »'],
  es: ['«', '»'],
  it: ['«', '»'],
  pt: ['«', '»'],
  ru: ['«', '»'],
  pl: ['„', '“'],
  cs: ['„', '“'],
  sk: ['„', '“'],
  'zh-CN': ['“', '”'],
  nl: ['“', '”'],
  ja: ['「', '」'],
  ko: ['“', '”']
}



export function autoReplacementPreview(language: string): string {
  const [open, close] = QUOTE_PREVIEW[language] ?? QUOTE_PREVIEW.en
  return `'x' → ${open}x${close}   |   -- → —   |   ... → …`
}
