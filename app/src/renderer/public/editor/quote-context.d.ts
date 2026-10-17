interface QuotePair {
  start: string
  end: string
  startReplace: string
  endReplace: string
}

interface QuoteReplacement {
  replacement: string
  backtrack: number
  provisional?: boolean
}

interface PendingQuote {
  before: string
  length: number
}

declare global {
  var NovalistQuoteContext: {
    replace(pair: QuotePair, before: string, typed: string, after: string): QuoteReplacement | null
    continueWord(pending: PendingQuote | null, before: string, typed: string): QuoteReplacement | null
  }
}

export type { PendingQuote }
