import { readFileSync } from 'node:fs'
import { stripTypeScriptTypes } from 'node:module'
import { runInNewContext } from 'node:vm'

export function loadRendererSource(path, dependencies = {}, globals = {}, sourceOverride) {
  const filename = new URL('../src/renderer/' + path, import.meta.url)
  let code = stripTypeScriptTypes(sourceOverride ?? readFileSync(filename, 'utf8'))
  const names = [...code.matchAll(/export\s+(?:async\s+)?(?:function|const|let|class)\s+(\w+)/g)].map((match) => match[1])
  code = code.replace(/^import\s+\{([^}]+)\}\s+from\s+'([^']+)'[ \t]*;?/gm,
    (_match, imports, name) => imports.replace(/[,\s]/g, '') ? `const {${imports}} = require('${name}');` : '')
    .replace(/^import\s+(\w+)\s+from\s+'([^']+)'[ \t]*;?/gm, (_match, name, path) => `const ${name} = require('${path}').default;`)
    .replace(/\bexport\s+/g, '')
  code += `\nObject.assign(module.exports, { ${names.join(', ')} });`
  const module = { exports: {} }
  runInNewContext(code, { module, console, setTimeout, clearTimeout,
    require(name) {
      if (!(name in dependencies)) throw new Error(`Unexpected dependency: ${name}`)
      return dependencies[name]
    }, ...globals }, { filename: filename.pathname })
  return module.exports
}
