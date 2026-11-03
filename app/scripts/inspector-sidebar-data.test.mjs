import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadBookScoped } from '../src/renderer/src/stores/bookScopedLoad.ts'

const turn = () => new Promise(resolve => setImmediate(resolve))
const compile = async (name, suffix = '') => (await transformWithEsbuild(
  readFileSync(new URL(`../src/renderer/src/shell/${name}.tsx`, import.meta.url), 'utf8') + suffix,
  `${name}.tsx`, { loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment' }
)).code
const linksCode = await compile('LinksPanel')
const notesCode = await compile('SceneNotesFields', '\nexport { useSceneNotes }\n')

function renderer(code, dependencies) {
  const slots = []
  let cursor = 0, effects = []
  const changed = (previous, deps) => !previous || deps.some((value, i) => !Object.is(value, previous.deps[i]))
  const hooks = {
    useState(initial) {
      const index = cursor++
      if (!(index in slots)) slots[index] = typeof initial === 'function' ? initial() : initial
      return [slots[index], value => { slots[index] = typeof value === 'function' ? value(slots[index]) : value }]
    },
    useRef(initial) { const index = cursor++; return slots[index] ??= { current: initial } },
    useCallback(callback, deps) {
      const index = cursor++
      if (changed(slots[index], deps)) slots[index] = { deps, callback }
      return slots[index].callback
    },
    useEffect(effect, deps) {
      const index = cursor++
      if (changed(slots[index], deps)) effects.push(() => {
        slots[index]?.cleanup?.()
        slots[index] = { deps, cleanup: effect() }
      })
    }
  }
  const module = { exports: {} }
  runInNewContext(code, { module, exports: module.exports,
    jsx: (type, props, ...children) => ({ type, props: props ?? {}, children }), fragment: 'fragment',
    require(name) {
      if (name === 'react') return hooks
      if (name in dependencies) return dependencies[name]
      throw new Error(`Unexpected dependency: ${name}`)
    }
  })
  return {
    render(name, props) {
      cursor = 0; effects = []
      const result = module.exports[name](props)
      effects.forEach(effect => effect())
      return result
    },
    unmount() { for (const slot of slots) slot?.cleanup?.() }
  }
}

function nodes(tree) {
  if (!tree || typeof tree !== 'object') return []
  return [tree, ...(tree.children ?? []).flat(Infinity).flatMap(nodes)]
}

function linksFixture({ delayed = false } = {}) {
  const calls = [], opened = [], views = []
  let release
  const gate = delayed ? new Promise(resolve => { release = resolve }) : Promise.resolve()
  const project = { isLoaded: true, workspaceBusy: false, projectPath: '/project', activeBookId: 'book', activeDraftId: 'draft', workspaceEpoch: 1, chapters: [], openScene() {} }
  const link = { id: 'link', kind: 'entity', targetId: 'mira', targetTitle: 'Mira', note: '' }
  const rendererState = renderer(linksCode, {
    'react-i18next': { useTranslation: () => ({ t: key => key }) },
    'lucide-react': { ArrowLeft: 'arrow', ArrowRight: 'arrow', Plus: 'plus', X: 'x' },
    '../stores/projectStore': { useProjectStore: Object.assign(select => select(project), { getState: () => ({ ...project }) }) },
    '../stores/bookScopedLoad': { loadBookScoped },
    '../stores/shellStore': { useShellStore: select => select({ setMainView: view => views.push(view) }) },
    '../stores/wikiStore': { useWikiStore: { getState: () => ({ openArticle: (...args) => opened.push(args) }) } },
    '../rpc/client': { rpc: { request: async (method, args) => {
      calls.push([method, ...args])
      if (method === 'links/list') return [link]
      if (method === 'links/backlinks') return []
      if (method === 'entities/list') { await gate; return args[0] === 'character' ? [{ id: 'mira', name: 'Mira' }] : [] }
      throw new Error(`Unexpected RPC ${method}`)
    } } }
  })
  const props = { chapterGuid: 'chapter', sceneId: 'scene' }
  const render = () => rendererState.render('LinksPanel', props)
  return { calls, opened, views, props, project, release, render, unmount: rendererState.unmount,
    button: () => nodes(render()).find(node => node.type === 'button' && node.props.className === 'links-target') }
}

test('existing entity links open Wiki before the add-link picker is used, and reuse resolved metadata', async () => {
  const f = linksFixture()
  f.render(); await turn()
  assert.equal(f.calls.filter(([method]) => method === 'entities/list').length, 0, 'picker data remains lazy')
  f.button().props.onClick()
  await turn()
  assert.deepEqual(f.opened.map(args => [...args]), [['character', 'mira']])
  assert.deepEqual(f.views, ['wiki'])
  f.button().props.onClick()
  await turn()
  assert.equal(f.calls.filter(([method]) => method === 'entities/list').length, 4)
})

for (const transition of ['scene', 'unmount', 'book', 'draft']) {
  test(`a linked entity response cannot navigate after ${transition} changes`, async () => {
    const f = linksFixture({ delayed: true })
    f.render(); await turn()
    f.button().props.onClick()
    assert.equal(f.calls.filter(([method]) => method === 'entities/list').length, 4)
    if (transition === 'scene') { f.props.sceneId = 'other'; f.render() }
    else if (transition === 'unmount') f.unmount()
    else if (transition === 'book') f.project.activeBookId = 'another-book'
    else f.project.activeDraftId = 'another-draft'
    f.release(); await turn()
    assert.deepEqual(f.opened, [])
    assert.deepEqual(f.views, [])
  })
}

function notesFixture() {
  const state = { openChapterGuid: 'chapter', openSceneId: 'first', chapters: [{ guid: 'chapter', scenes: [
    { id: 'first', synopsis: '' }, { id: 'second', synopsis: '' }
  ] }] }
  const reads = []
  let revision = 0
  const props = { definitions: [], sceneValues: {}, load() {} }
  const f = renderer(notesCode, {
    'react-i18next': { useTranslation: () => ({ t: key => key }) },
    '../stores/projectStore': { useProjectStore: select => select(state) },
    '../stores/manuscriptPropsStore': { useManuscriptPropsStore: Object.assign(select => select(props), { getState: () => props }) },
    '../collaboration/sharedProjectNotifications': { useSharedProjectRevision: () => revision },
    '../rpc/client': { rpc: { request: (method, args) => {
      assert.equal(method, 'scenes/getMeta')
      return new Promise(resolve => reads.push({ scene: args[1], resolve }))
    } } },
    '../collaboration/SharedPropertyField': {}, './SceneCastPicker': {},
    '../collaboration/SharedMetadataField': {}, '../collaboration/ScenePlanningText': {}
  })
  return { state, reads, render: () => f.render('useSceneNotes'), unmount: f.unmount,
    revise() { revision++ } }
}

test('reopening scene properties restores saved synopsis from metadata despite a stale scene summary', async () => {
  const f = notesFixture()
  f.render()
  f.reads[0].resolve({ synopsis: 'Saved synopsis', goal: 'Saved goal' }); await turn()
  assert.equal(f.render().synopsis, 'Saved synopsis')
  f.state.openSceneId = 'second'; f.render()
  f.reads[1].resolve({ synopsis: '' }); await turn()
  assert.equal(f.render().synopsis, '')
  f.state.openSceneId = 'first'; f.render()
  f.reads[2].resolve({ synopsis: 'Saved synopsis' }); await turn()
  assert.equal(f.render().synopsis, 'Saved synopsis')
})

test('a same-scene metadata refresh preserves newer local synopsis edits', async () => {
  const f = notesFixture()
  f.render(); f.reads[0].resolve({ synopsis: 'Saved synopsis' }); await turn()
  f.render().setSynopsis('New local draft')
  f.revise(); f.render()
  f.reads[1].resolve({ synopsis: 'Older snapshot' }); await turn()
  assert.equal(f.render().synopsis, 'New local draft')
})

test('metadata from a departed scene cannot replace the current synopsis', async () => {
  const f = notesFixture()
  f.render()
  f.state.openSceneId = 'second'; f.render()
  f.reads[1].resolve({ synopsis: 'Current scene' }); await turn()
  f.reads[0].resolve({ synopsis: 'Departed scene' }); await turn()
  assert.equal(f.render().synopsis, 'Current scene')
})
