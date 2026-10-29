import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadRendererSource } from './renderer-source-loader.mjs'

const cases = [
  { name: 'project creation', path: 'shell/CreateProjectDialog.tsx', component: 'CreateProjectDialog' },
  { name: 'map image picker', path: 'views/maps/MapsView.tsx', component: 'MapsView' },
  { name: 'plotline custom fields', path: 'views/plotgrid/PlotGridView.tsx', component: 'PlotGridDialogs' },
  { name: 'Codex field arrangement', path: 'views/codex/ArrangeFieldsDialog.tsx', component: 'ArrangeFieldsDialog' },
  { name: 'cleanup', path: 'shell/CleanupDialog.tsx', component: 'CleanupDialog' },
  { name: 'draft comparison', path: 'shell/DraftCompareDialog.tsx', component: 'DraftCompareDialog' },
  { name: 'plugin import', path: 'shell/ImportPluginDialog.tsx', component: 'ImportPluginDialog', busyState: 7 },
  { name: 'pane layouts', path: 'shell/PaneLayoutsDialog.tsx', component: 'PaneLayoutsDialog' },
  { name: 'date shifting', path: 'shell/ShiftDatesDialog.tsx', component: 'ShiftDatesDialog' },
  { name: 'snapshot comparison', path: 'shell/SnapshotCompareDialog.tsx', component: 'SnapshotCompareDialog' },
  { name: 'project premise setup', path: 'shell/SnowflakeSetup.tsx', component: 'SnowflakeSetup', busyState: 5 },
  { name: 'writing sprint', path: 'shell/SprintPanel.tsx', component: 'SprintPanel' },
  { name: 'workspace layouts', path: 'shell/WorkspaceLayoutsDialog.tsx', component: 'WorkspaceLayoutsDialog' },
  { name: 'backup restoration', path: 'shell/RestoreBackupDialog.tsx', component: 'RestoreBackupDialog', busyState: 3 },
  { name: 'quick capture', path: 'shell/QuickCapture.tsx', component: 'QuickCapture' },
  { name: 'Codex type manager', path: 'views/codex/CustomTypeManager.tsx', component: 'CustomTypeManager' },
  { name: 'Codex create', path: 'views/codex/CodexView.tsx', component: 'CodexCreateDialog', private: true },
  { name: 'Codex wizard', path: 'views/codex/WizardDialog.tsx', component: 'WizardDialog' },
  { name: 'entity gallery', path: 'views/codex/EntityImages.tsx', component: 'EntityImages', states: { 0: true, 2: 'swap.png' }, changes: [[0, false], [2, null]] },
  { name: 'entity image URL', path: 'views/codex/EntityImages.tsx', component: 'EntityImages', states: { 3: true }, changes: [[3, false]] },
  { name: 'override gallery', path: 'views/codex/OverrideImages.tsx', component: 'OverrideImages', states: { 0: true }, changes: [[0, false]] },
  { name: 'override image URL', path: 'views/codex/OverrideImages.tsx', component: 'OverrideImages', states: { 2: true }, changes: [[2, false]] },
  { name: 'writing options', path: 'views/editor/EditorToolbar.tsx', component: 'EditorToolbar', states: { 0: true }, changes: [[0, false]] },
  { name: 'map scale', path: 'views/maps/MapScaleDialog.tsx', component: 'MapScaleDialog' },
  { name: 'map entity preview', path: 'views/maps/MapCanvas.tsx', component: 'MapCanvas' },
  { name: 'voice design', path: 'views/narration/NarrationDesignDialog.tsx', component: 'DesignDialog' },
  { name: 'story structure', path: 'views/timeline/StructureEditor.tsx', component: 'StructureEditor' },
  { name: 'timeline event', path: 'views/timeline/TimelineEventEditor.tsx', component: 'TimelineEventEditor' },
  { name: 'entity template', path: 'views/settings/TemplatesCard.tsx', component: 'TemplatesCard', states: {
    2: { type: 'character', record: { name: 'Template' }, knownFields: [], known: {}, customFields: [], props: [], sections: [] }
  }, changes: [[2, null]] },
  { name: 'project overview', path: 'shell/StatusBar.tsx', component: 'StatusOverview', private: true },
  { name: 'extension wizard', path: 'shell/ExtensionWizardHost.tsx', component: 'ExtensionWizardHost' },
  { name: 'cancellable progress', path: 'shell/BusyProgressHost.tsx', component: 'BusyProgressHost', busyState: -1 }
]

for (const entry of cases) {
  const source = readFileSync(new URL('../src/renderer/src/' + entry.path, import.meta.url), 'utf8')
  entry.code = (await transformWithEsbuild(
    source + (entry.private || entry.component === 'PlotGridDialogs' ? `\nexport { ${entry.component} }` : ''), entry.path,
    { loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment' }
  )).code
}

function testDependencies(dismiss, busy) {
  const project = { chapters: [], openChapterGuid: null, projectPath: 'project' }
  const wizard = { token: 'wizard', definition: { id: 'wizard', displayName: 'Wizard', steps: [{ id: 'name', title: 'Name', kind: 'text' }] } }
  return {
    'react-i18next': { useTranslation: () => ({ t: key => key }) },
    './mapViewModel': { useMapViewModel: () => ({ maps: [], imagePicker: [], t: key => key, setImagePicker: dismiss }) },
    projectStore: { useProjectStore: select => select(project) },
    settingsStore: { useSettingsStore: select => select({}) },
    shellStore: { useShellStore: select => select({ layouts: [] }), matchingLayout: () => null, DEFAULT_LAYOUT: { name: 'Default' } },
    layoutStore: { useLayoutStore: select => select({ layouts: [] }) },
    codexStore: { useCodexStore: select => select({ entityType: 'character', selectedId: 'entity', selectedRecord: { images: [] } }) },
    dictationStore: { useDictation: select => select({}) },
    narrationStore: { useNarrationStore: select => select({ engines: [], closeBrief: dismiss }) },
    hostBridgeStore: { useHostBridgeStore: select => select({ wizard, closeWizard: dismiss, cancelProgress: dismiss,
      progress: [{ token: 'progress', title: 'Task', progress: 0, details: [], allowCancel: !busy }] }) },
    client: { rpc: { notify() {} } },
    sprintStore: { useSprintStore: select => select({ running: false, startedAt: null, bankedSeconds: 0, targetMinutes: 25, history: [], summary: null }),
      elapsedSeconds: () => 0, sprintWords: () => 0, formatDuration: () => '0:00' }
  }
}

function fixture(entry, platform = 'android', busy = false) {
  let closed = false
  const dismiss = () => { closed = true }
  const window = new EventTarget()
  window.novalist = { platform }
  let dialog
  const bridge = loadRendererSource('src/mobile/backEvents.ts', {}, {
    window, Event,
    KeyboardEvent: class extends Event { constructor(type, options) { super(type, options); this.key = options.key } },
    document: { querySelector: () => null, querySelectorAll: () => ({ length: 1, item: () => dialog }), activeElement: null }
  })
  const module = { exports: {} }
  let stateIndex = 0
  const updates = []
  const dependencies = testDependencies(dismiss, busy)
  const jsx = (type, props, ...children) => ({ type, props, children })
  runInNewContext(entry.code, {
    module, exports: module.exports, window, Event, jsx, fragment: 'fragment',
    require(name) {
      if (name.endsWith('/mobile/backEvents')) return bridge
      if (name === 'react') return {
        useEffect() {}, useRef: () => ({ current: null }), useCallback: fn => fn, useMemo: fn => fn(),
        useState(initial) {
          const index = stateIndex++
          const value = busy && index === (entry.busyState ?? 6) ? true
            : Object.hasOwn(entry.states ?? {}, index) ? entry.states[index] : typeof initial === 'function' ? initial() : initial
          return [value, next => updates.push([index, next])]
        }
      }
      const dependency = dependencies[name] ?? dependencies[name.split('/').at(-1)]
      if (dependency) return dependency
      return new Proxy({}, { get: (_target, key) => key.startsWith('use') ? () => {} : key })
    }
  })
  const tree = module.exports[entry.component]({ onClose: dismiss, onCancel: dismiss, onDone: dismiss, setPeek: dismiss, t: key => key,
    entityType: 'character', fields: ['name', 'age'], labels: {},
    pending: { kind: 'fields', id: 'plotline', name: 'Thread' }, setPending: dismiss,
    types: [], templates: [], timelines: [], anchors: [], initial: null, sceneIds: [],
    scope: { chapter: 'chapter', scene: null }, baseImages: [],
    formatting: { alignment: 'left', paragraphStyle: '' }, peek: { name: 'Entity' },
    brief: { characterId: 'character', name: 'Entity', description: 'Voice', sampleLines: [] },
    steps: [{ id: 'name', title: 'Name', kind: 'text' }],
    state: { t: key => key, chapters: [], overviewOpen: true, setOverviewOpen: dismiss, sceneCount: 0, avgChapterWords: 0, maxChapterWords: 0 }
  })
  function findDialog(node) {
    if (!node || typeof node !== 'object') return null
    if (node.props?.role === 'dialog') return node
    for (const child of (node.children ?? []).flat(Infinity)) {
      const found = findDialog(child)
      if (found) return found
    }
    return null
  }
  const card = findDialog(tree)
  assert.ok(card, 'the real component renders its dialog')
  dialog = new EventTarget()
  dialog.addEventListener('keydown', event => (card.props.onKeyDownCapture ?? card.props.onKeyDown)?.(event))
  return { back: bridge.dispatchAndroidBack, closed: () => closed || !!entry.changes?.every(([index, value]) => updates.some(([changed, next]) => index === changed && value === next)), updates }
}

for (const entry of cases) {
  test(`Android Back closes ${entry.name}`, async () => {
    const f = fixture(entry)
    assert.equal(f.back(), true)
    await Promise.resolve()
    assert.equal(f.closed(), true)
  })
  test(`Android Back handling does not change ${entry.name} keyboard behavior on desktop`, () => {
    const f = fixture(entry, 'win32')
    f.back()
    assert.equal(f.closed(), false)
  })
}

test('Android Back cannot close project creation while it is saving', () => {
  const f = fixture(cases[0], 'android', true)
  assert.equal(f.back(), true)
  assert.equal(f.closed(), false)
})

for (const entry of cases.filter(entry => entry.busyState !== undefined)) {
  test(`Android Back respects the busy cancellation guard for ${entry.name}`, () => {
    const f = fixture(entry, 'android', true)
    assert.equal(f.back(), true)
    assert.equal(f.closed(), false)
  })
}
