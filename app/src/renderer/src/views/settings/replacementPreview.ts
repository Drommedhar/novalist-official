import type { ReplacementRule } from './AutoReplacementsCard'

/**
 * What one rule would do to a sample, as the writer types it.
 *
 * This is a port of tryAutoReplace in the editor page, which cannot be imported
 * from here: it lives in a standalone HTML document loaded into an iframe, with
 * no bundler between them. A preview that disagreed with the editor would be
 * worse than none - the writer would tune a rule against a lie - so the two are
 * held together by an end-to-end test that types the same sample into the real
 * editor and compares what lands with what this produced.
 *
 * Typing-time behaviour, not cleanup behaviour: a rule fires where its match
 * ends, because that is the moment the characters exist. Running the pattern
 * over the whole sample at once would show replacements the editor would not
 * make until the writer typed past them.
 */

/** Mirrors REGEX_LOOKBEHIND in editor.html. */
const REGEX_LOOKBEHIND = 120

/** Longer than this and the preview stops being a preview. */
export const MAX_SAMPLE = 120

/**
 * The whole simulation's budget. A pattern is the writer's to write, and one
 * that backtracks badly would otherwise lock the settings screen while they
 * are still typing it.
 */
const BUDGET_MS = 25

export interface RulePreview {
  /** The sample as the editor would leave it. */
  result: string
  /** What the rule matched, the first time it fired. */
  matched: string | null
  /** The capture groups of that first match, for a pattern rule. */
  groups: string[]
  /** Why the rule cannot run at all, if it cannot. */
  error: 'badPattern' | 'matchesNothing' | 'tooSlow' | null
}

/** Puts the captured groups into a replacement: $1..$9, and $$ for a literal $. */
export function expandCaptures(template: string, match: RegExpExecArray): string {
  return template.replace(/\$(\$|\d)/g, (_whole, token: string) =>
    token === '$' ? '$' : (match[Number(token)] ?? '')
  )
}

function countOccurrences(text: string, token: string): number {
  if (!token) return 0
  let count = 0
  let at = text.indexOf(token)
  while (at !== -1) {
    count += 1
    at = text.indexOf(token, at + token.length)
  }
  return count
}

/** One keystroke's worth of the editor's decision, or null for "type it as-is". */
function attempt(
  rule: ReplacementRule,
  regex: RegExp | null,
  lineText: string,
  typed: string
): { replacement: string; backtrack: number; match: RegExpExecArray | null } | null {
  if (regex) {
    const tail = lineText.length > REGEX_LOOKBEHIND ? lineText.slice(-REGEX_LOOKBEHIND) : lineText
    const match = regex.exec(tail)
    if (match && match[0].length >= typed.length) {
      return {
        replacement: expandCaptures(rule.startReplace, match),
        backtrack: match[0].length - typed.length,
        match
      }
    }
    return null
  }

  // The alternating form: one trigger, an opening and a closing replacement,
  // chosen by which of the two the line is already carrying more of.
  if (rule.start === rule.end && rule.startReplace !== rule.endReplace) {
    if (rule.end && lineText.endsWith(rule.end)) {
      const opens = countOccurrences(lineText, rule.startReplace)
      const closes = countOccurrences(lineText, rule.endReplace)
      if (opens > closes) {
        return {
          replacement: rule.endReplace,
          backtrack: rule.end.length - typed.length,
          match: null
        }
      }
    }
  }
  if (rule.start && lineText.endsWith(rule.start)) {
    return {
      replacement: rule.startReplace,
      backtrack: rule.start.length - typed.length,
      match: null
    }
  }
  return null
}

/** Runs the sample through one rule, a keystroke at a time. */
export function previewRule(rule: ReplacementRule, sample: string): RulePreview {
  const empty: RulePreview = { result: sample, matched: null, groups: [], error: null }
  if (!rule.start || !sample) return empty

  let regex: RegExp | null = null
  if (rule.kind === 'regex') {
    try {
      regex = new RegExp(`(?:${rule.start})$`)
    } catch {
      return { ...empty, error: 'badPattern' }
    }
    // The same refusal the backend makes: a pattern matching the empty string
    // fires before every keystroke, forever.
    if (regex.test('')) return { ...empty, error: 'matchesNothing' }
  }

  const started = performance.now()
  let out = ''
  let matched: string | null = null
  let groups: string[] = []

  for (const character of sample.slice(0, MAX_SAMPLE)) {
    if (performance.now() - started > BUDGET_MS) {
      return { result: out, matched, groups, error: 'tooSlow' }
    }
    const fired = attempt(rule, regex, out + character, character)
    if (!fired) {
      out += character
      continue
    }
    out = out.slice(0, out.length - fired.backtrack) + fired.replacement
    if (matched === null) {
      matched = fired.match ? fired.match[0] : rule.start
      groups = fired.match ? fired.match.slice(1).map((g) => g ?? '') : []
    }
  }

  return { result: out, matched, groups, error: null }
}
