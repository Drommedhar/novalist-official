import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const components = new Map()
for (const name of ['EntityDetailFields', 'EntityListsEditor', 'OverrideRelationships']) {
  const source = readFileSync(new URL(`../src/renderer/src/views/codex/${name}.tsx`, import.meta.url), 'utf8')
  const { code } = await transformWithEsbuild(source, `${name}.tsx`, {
    loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment'
  })
  components.set(name, code)
}

function fixture(entityType = 'character', customDef, component = 'EntityDetailFields') {
  const slots = [], effects = [], calls = [], timers = new Map()
  let index = 0, project = { projectPath: '/one', activeBookId: 'book', activeDraftId: 'draft', workspaceEpoch: 1 }
  let props = { entityType, customDef, record: { id: 'one', name: 'Saved name', group: 'Saved group', parent: 'Saved parent', fields: { friend: 'Saved friend' }, relationships: [{ role: 'Ally', target: 'Saved target', inverseRole: 'Friend' }] }, updateField: async () => {} }
  const react = {
    useId: () => 'field',
    useState(initial) {
      const at = index++
      if (!(at in slots)) slots[at] = typeof initial === 'function' ? initial() : initial
      return [slots[at], next => { slots[at] = typeof next === 'function' ? next(slots[at]) : next }]
    },
    useRef(initial) { const at = index++; return slots[at] ??= { current: initial } },
    useCallback(callback, deps) {
      const at = index++, old = slots[at]
      if (old && deps.every((value, i) => value === old.deps[i])) return old.callback
      slots[at] = { deps, callback }; return callback
    },
    useEffect(effect, deps) {
      const at = index++, old = slots[at]
      if (old && deps.every((value, i) => value === old.deps[i])) return
      effects.push(() => { old?.cleanup?.(); slots[at] = { deps, cleanup: effect() } })
    }
  }
  const rpc = { request(method, params, options) {
    if (method === 'sheets/get') return Promise.resolve({ hidden: [], order: [] })
    if (method === 'wiki/generatorAvailable') return Promise.resolve(false)
    return new Promise((resolve, reject) => {
      calls.push({ method, params, signal: options?.signal, resolve, reject })
      options?.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
    })
  } }
  const codex = () => ({ entityType: props.entityType, selectedId: props.record.id, selectedRecord: props.record, restoredRevision: 0 })
  const dependencies = { react, '../../rpc/client': { rpc },
    '../../stores/projectStore': { useProjectStore: select => select(project) },
    '../../stores/codexStore': { useCodexStore: Object.assign(select => select(codex()), { getState: codex, setState() {} }) },
    '../../stores/pendingWrites': { persistPendingWrite: (_key, action) => action(), registerPendingWrite: () => () => {} },
    './useEntitySections': { useEntitySections: () => ({ sections: [], setSections() {}, flushSections: async () => {} }) },
    './overrideModel': { apply() {} } }
  const module = { exports: {} }
  runInNewContext(components.get(component), { module, exports: module.exports, AbortController,
    setTimeout: callback => { const timer = {}; timers.set(timer, callback); return timer }, clearTimeout: timer => timers.delete(timer),
    jsx: (type, props, ...children) => ({ type, props, children }), fragment: 'fragment',
    require(name) {
      if (name in dependencies) return dependencies[name]
      if (name === 'react-i18next') return { useTranslation: () => ({ t: key => key }) }
      if (name === './useEntityFieldSuggestions') return loadRendererSource('src/views/codex/useEntityFieldSuggestions.ts', dependencies, { AbortController })
      return {}
    }
  })
  return { calls,
    render() {
      index = 0
      const tree = module.exports[component]({ ...props, selectedId: props.record.id,
        scope: { chapter: 'chapter', scene: 'scene' }, baseRelationships: props.record.relationships })
      effects.splice(0).forEach(effect => effect()); return tree
    },
    flushTimers() { const callbacks = [...timers.values()]; timers.clear(); callbacks.forEach(callback => callback()) },
    record(id) { props = { ...props, record: { ...props.record, id } } },
    project(path) { project = { ...project, projectPath: path } },
    unmount() { slots.forEach(slot => slot?.cleanup?.()) }
  }
}

function nodes(tree) {
  return [tree, ...(tree?.children ?? []).flat(Infinity).filter(child => child && typeof child === 'object').flatMap(nodes)]
}
const input = (tree, label) => nodes(tree).find(node => node.type === 'input' && node.props['aria-label'] === label)

for (const [type, label, value, method, params, customDef] of [
  ['character', 'entityEditor.groupPlaceholder', 'Saved group', 'entities/groups', undefined],
  ['location', 'entityEditor.parentLocation', 'Saved parent', 'entities/list', ['location']],
  ['custom', 'Friend', 'Saved friend', 'entities/list', ['character'], { defaultFields: [{ key: 'friend', displayName: 'Friend', type: 'EntityRef', enumOptions: ['character'] }] }]
]) test(`${type} suggestions wait for focus while the stored field stays visible`, async () => {
  const f = fixture(type, customDef)
  const field = input(f.render(), label)
  assert.equal(field.props.defaultValue, value)
  assert.equal(f.calls.length, 0, 'mounting detail fields must not scan entity lists')
  field.props.onFocus()
  field.props.onFocus()
  assert.equal(f.calls.length, 1, 'repeated focus shares the request')
  assert.equal(f.calls[0].method, method)
  assert.deepEqual(f.calls[0].params && [...f.calls[0].params], params)
  f.calls[0].resolve(method === 'entities/groups' ? ['Other group'] : [{ name: 'Other name' }])
  await turn()
  input(f.render(), label).props.onFocus()
  assert.equal(f.calls.length, 1, 'completed suggestions are reused in the same record')
  assert.equal(input(f.render(), label).props.defaultValue, value)
})

test('record switches, project switches and unmount abort pending picker reads', async () => {
  const f = fixture()
  const focus = () => input(f.render(), 'entityEditor.groupPlaceholder').props.onFocus()
  focus()
  f.record('two')
  f.render()
  assert.equal(f.calls[0].signal.aborted, true)
  focus()
  f.project('/two')
  f.render()
  assert.equal(f.calls[1].signal.aborted, true)
  focus()
  f.unmount()
  assert.equal(f.calls[2].signal.aborted, true)
  await turn()
})

for (const component of ['EntityListsEditor', 'OverrideRelationships']) {
  const targetInput = tree => nodes(tree).find(node => node.type === 'input' && node.props.placeholder === 'entityEditor.targetNames')
  test(`${component} loads relationship suggestions only on focus and preserves row autosave`, async () => {
    const f = fixture('character', undefined, component)
    f.render()
    const target = targetInput(f.render())
    assert.equal(target.props.value, 'Saved target')
    assert.equal(f.calls.length, 0, 'stored relationship rows need no suggestion scan')
    target.props.onFocus()
    target.props.onFocus()
    assert.equal(f.calls.length, 1)
    assert.equal(f.calls[0].method, 'entities/relationshipSuggestions')
    f.calls[0].resolve({ characterNames: ['Other target'], roles: ['Other role'] })
    await turn()
    targetInput(f.render()).props.onFocus()
    assert.equal(f.calls.length, 1)
    targetInput(f.render()).props.onChange({ target: { value: 'Edited target' } })
    targetInput(f.render()).props.onBlur()
    f.flushTimers()
    const save = f.calls[1]
    assert.equal(save.method, component === 'EntityListsEditor' ? 'entities/setRelationships' : 'entities/setOverrideRelationships')
    assert.equal(save.params[component === 'EntityListsEditor' ? 1 : 3][0].target, 'Edited target')
  })
  test(`${component} cancels suggestion reads when its entry changes or unmounts`, async () => {
    const f = fixture('character', undefined, component)
    f.render()
    targetInput(f.render()).props.onFocus()
    f.record('two')
    f.render()
    assert.equal(f.calls[0].signal.aborted, true)
    targetInput(f.render()).props.onFocus()
    f.unmount()
    assert.equal(f.calls[1].signal.aborted, true)
    await turn()
  })
}
