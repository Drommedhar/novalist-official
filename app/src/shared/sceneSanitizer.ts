import type { Config, DOMPurify } from 'dompurify'

export function createSceneSanitizer(window: { document: Document }, purify: DOMPurify): {
  sanitizeSceneHtml(html: string): string
  sanitizeSceneFragment(html: string): DocumentFragment
} {
  const document = window.document.implementation.createHTMLDocument('')
  const form = document.createElement('form')
  const windowNames = new Set<string>()
  for (let owner: object | null = window; owner; owner = Object.getPrototypeOf(owner)) {
    for (const name of Object.getOwnPropertyNames(owner)) windowNames.add(name)
  }
  // Preserve prose anchor IDs that collide only with form properties; forms
  // are forbidden, while document and window clobbering protection remains.
  purify.addHook('uponSanitizeAttribute', (node, attribute, config) => {
    if (node.namespaceURI === 'http://www.w3.org/1999/xhtml'
      && attribute.attrName === 'id' && attribute.attrValue in form
      && !(attribute.attrValue in document) && !windowNames.has(attribute.attrValue)
      && !config.FORBID_ATTR?.includes('id') && config.FORBID_TAGS?.includes('form')) {
      attribute.forceKeepAttr = true
    }
  })
  return {
    sanitizeSceneHtml: html => purify.sanitize(html || '', options()),
    sanitizeSceneFragment: html => purify.sanitize(html || '', { ...options(), RETURN_DOM_FRAGMENT: true })
  }
}

function options(): Config {
  return {
    USE_PROFILES: { html: true },
    ADD_ATTR: ['contenteditable'],
    FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'option'],
    ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp|matrix|novalist|novalist-project|novalist-asset|novalist-audio):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i
  }
}
