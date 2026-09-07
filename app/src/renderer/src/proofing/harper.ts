import { Dialect, SuggestionKind, WorkerLinter } from 'harper.js'
import { binaryInlined } from 'harper.js/binaryInlined'
import type { GrammarIssue } from './grammar'

// The binary is bundled, including on first use; no CDN or dictionary download.
// This module is loaded only when the writer selects Harper.
let linter: WorkerLinter | undefined
let dictionary = ''
let pending: Promise<unknown> = Promise.resolve()

function dialectFor(language: string): Dialect {
  switch (language.toLowerCase()) {
    case 'en-gb':
    case 'en-nz':
    case 'en-za': return Dialect.British
    case 'en-au': return Dialect.Australian
    case 'en-ca': return Dialect.Canadian
    case 'en-in': return Dialect.Indian
    default: return Dialect.American
  }
}

/** Serialize configuration and checks so split panes cannot change a running check's dialect. */
export function checkHarper(text: string, language: string, words: string[]): Promise<GrammarIssue[]> {
  const task = pending.then(async () => {
    linter ??= new WorkerLinter({ binary: binaryInlined, dialect: dialectFor(language) })
    if (await linter.getDialect() !== dialectFor(language)) {
      await linter.setDialect(dialectFor(language))
      // Harper rebuilds its internal linter when the dialect changes, which
      // also drops imported words even if our saved dictionary is unchanged.
      dictionary = ''
    }
    const nextDictionary = JSON.stringify(words)
    if (dictionary !== nextDictionary) {
      await linter.clearWords()
      await linter.importWords(words)
      dictionary = nextDictionary
    }

    const lints = await linter.lint(text, { language: 'plaintext' })
    return lints.map((lint): GrammarIssue => {
      const span = lint.span()
      const suggestions = lint.suggestions()
      try {
        // harper.js exposes UTF-16 offsets, matching the editor's plain-text
        // map even when the scene contains characters represented by pairs.
        const original = text.slice(span.start, span.end)
        const kind = lint.lint_kind()
        return {
          message: lint.message(),
          offset: span.start,
          length: span.end - span.start,
          type: kind === 'Spelling' ? 'spelling'
            : ['Style', 'WordChoice', 'Formatting'].includes(kind) ? 'style' : 'grammar',
          replacements: suggestions.slice(0, 5).map((suggestion) =>
            suggestion.kind() === SuggestionKind.InsertAfter
              ? original + suggestion.get_replacement_text()
              : suggestion.get_replacement_text()
          )
        }
      } finally {
        suggestions.forEach((suggestion) => suggestion.free())
        span.free()
        lint.free()
      }
    })
  })
  pending = task.catch(() => {})
  return task
}
