/**
 * Handing the interface theme to an extension's webview.
 *
 * An extension panel is an iframe, and a CSS custom property does not cross a
 * document boundary - `:root { --nl-* }` in the shell is simply not there
 * inside the frame. So every extension wrote its own palette and its own type
 * scale, which is why the AI panels were VS Code grey on a Novalist that is
 * Ink Night and gilt, and why picking Discord or high-contrast changed
 * everything except them.
 *
 * The editor frame has had a bridge for this since it shipped
 * (`pushEditorTheme`), but it hands over nine named colours because that is all
 * a text surface needs. A panel is chrome: it wants surfaces, borders, the
 * accent, the type scale, spacing and radius. So this sends the whole `--nl-*`
 * set rather than a chosen few - there is then nothing for an extension author
 * to look up, and a token added later reaches them without a release.
 */

/**
 * Every `--nl-*` token the loaded stylesheets declare, resolved against the
 * live theme.
 *
 * The names come from the stylesheets rather than from the computed style:
 * Chromium does not enumerate custom properties on a CSSStyleDeclaration read
 * back from `getComputedStyle`, so iterating it yields the standard properties
 * and none of ours. Reading the sheets also picks up a user theme or an
 * extension-contributed one, which is the point - a theme that invents a token
 * is carried across with the rest.
 */
export function themeTokens(): Record<string, string> {
  const resolved = getComputedStyle(document.documentElement)
  const tokens: Record<string, string> = {}

  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList
    try {
      rules = sheet.cssRules
    } catch {
      // A stylesheet from another origin refuses to be read. None of ours are,
      // so this is a theme served from somewhere unexpected rather than an
      // error worth reporting.
      continue
    }

    for (const rule of Array.from(rules)) {
      const style = (rule as CSSStyleRule).style
      if (!style) continue
      for (const property of Array.from(style)) {
        if (!property.startsWith('--nl-')) continue
        const value = resolved.getPropertyValue(property).trim()
        if (value.length > 0) tokens[property] = value
      }
    }
  }

  return tokens
}

/** Sends the current theme into an extension frame. */
export function postThemeToFrame(frame: Window | null | undefined): void {
  frame?.postMessage({ novalistTheme: themeTokens() }, '*')
}

/**
 * Re-sends the theme whenever it changes, and returns the stop function.
 *
 * `data-theme` is what the shell sets when the writer picks a theme, and
 * `data-material` when the macOS chrome turns translucent - both restate
 * tokens, so both have to reach a panel that is already open.
 */
export function watchTheme(send: () => void): () => void {
  const observer = new MutationObserver(send)
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-theme', 'data-material']
  })
  return () => observer.disconnect()
}
