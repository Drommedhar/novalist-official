import { readFileSync } from 'node:fs'

const mapRoot = new URL('../src/renderer/public/map/', import.meta.url)
const html = readFileSync(new URL('map.html', mapRoot), 'utf8')
const importMap = html.match(/<script type="importmap">([\s\S]*?)<\/script>/)
if (!importMap) throw new Error('Map page is missing its browser import map')
const { imports } = JSON.parse(importMap[1])

export function resolve(specifier, context, nextResolve) {
  if (context.parentURL?.startsWith(mapRoot.href) && Object.hasOwn(imports, specifier)) {
    return nextResolve(new URL(imports[specifier], mapRoot).href, context)
  }
  return nextResolve(specifier, context)
}
