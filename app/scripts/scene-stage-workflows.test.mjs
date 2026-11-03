import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { transformSync } from 'esbuild'
import { create } from 'zustand'
import { renderToStaticMarkup } from 'react-dom/server'
import { createElement } from 'react'
import * as React from 'react'
import * as jsx from 'react/jsx-runtime'
import * as icons from 'lucide-react'
import { loadRendererSource } from './renderer-source-loader.mjs'

const t = (key, values = {}) => key + (values.status ? `: ${values.status}` : '')
function component(path, dependencies = {}) {
  const source = readFileSync(new URL('../src/renderer/src/' + path, import.meta.url), 'utf8')
  const { code } = transformSync(source, { loader: 'tsx', format: 'cjs', jsx: 'automatic' })
  const module = { exports: {} }
  const modules = { react: React, 'react/jsx-runtime': jsx, 'lucide-react': icons,
    'react-i18next': { useTranslation: () => ({ t }) }, ...dependencies }
  runInNewContext(code, { module, exports: module.exports, window: { novalist: { isMobile: true } },
    require: name => {
      if (!(name in modules)) throw new Error(`Unexpected dependency: ${name}`)
      return modules[name]
    }
  })
  return module.exports
}

function filterMarkup(status = '', stage = '') {
  const filter = { status, stage, character: '', location: '', plotline: '' }
  const { FilterBar } = component('shell/FilterBar.tsx', {
    '../rpc/client': { rpc: {} },
    '../stores/projectStore': { useProjectStore: selector => selector({ projectPath: '/book' }), useBookScope: () => '/book' },
    '../stores/stageStore': { useStageStore: selector => selector({ stages: [{ key: 'firstDraft', label: 'First draft' }, { key: 'outline', label: 'Outline' }] }) },
    './useIsPhone': { useIsPhone: () => false },
    './DesktopViewFrame': { DesktopViewActions: ({ children }) => children },
    '../stores/filterStore': { useFilterStore: selector => selector({ filter, presets: [] }),
      isEmptyFilter: value => Object.values(value).every(item => !item),
      activeCount: value => Object.values(value).filter(Boolean).length }
  })
  return renderToStaticMarkup(createElement(FilterBar))
}

test('ordinary writing filters offer scene stages without a second chapter-status picker', () => {
  const html = filterMarkup()
  assert.match(html, /value="firstDraft"/)
  assert.match(html, /filters.stage/)
  assert.doesNotMatch(html, /filters.status/)
  assert.doesNotMatch(html, /value="FirstDraft"/)
})

test('an old preset exposes its chapter restriction without offering a new chapter-status picker', () => {
  const html = filterMarkup('Revised', 'firstDraft')
  assert.match(html, /filters.legacyChapterStatus/)
  assert.match(html, /dashboard.statusRevised/)
  assert.match(html, /filters.clearChapterStatus/)
  assert.doesNotMatch(html, /value="FirstDraft"/)
})

test('legacy presets retain their chapter-status semantics instead of overwriting scene stages', () => {
  const preset = { name: 'Old revision pass', filter: { status: 'Revised', stage: 'firstDraft', character: '', location: '', plotline: '' } }
  const { useFilterStore } = loadRendererSource('src/stores/filterStore.ts', { zustand: { create } }, {
    localStorage: { getItem: () => JSON.stringify([preset]), setItem() {} }
  })
  useFilterStore.getState().loadPresets('/book')
  useFilterStore.getState().apply(preset.name)
  assert.equal(useFilterStore.getState().filter.status, 'Revised')
  assert.equal(useFilterStore.getState().filter.stage, 'firstDraft')
  useFilterStore.getState().set({ status: '' })
  assert.equal(useFilterStore.getState().filter.stage, 'firstDraft')
})

test('scene-stage card colors use the configured scene color and ignore legacy chapter status', () => {
  const { sceneColour } = loadRendererSource('src/views/manuscript/sceneColour.ts')
  assert.equal(sceneColour('stage', { stageColor: '#c98b3a', status: 'Outline' }), '#c98b3a')
  assert.equal(sceneColour('stage', { status: 'FirstDraft' }), undefined)
  assert.equal(sceneColour('label', { labelColor: '#123456' }), '#123456')
})

test('chapter rows have no writing-status control', () => {
  const { BinderChapters } = component('shell/BinderChapters.tsx', {
    './BinderSceneRow': { BinderSceneRow: () => null }, './MotionPresence': { MotionPresence: ({ children }) => children }
  })
  const html = renderToStaticMarkup(createElement(BinderChapters, { state: {
    t, binderTab: 'chapters', isMobile: false, collapsed: { chapter: true },
    chapters: [{ guid: 'chapter', title: 'Chapter', act: '', status: 'Revised', scenes: [] }], scenesOf: () => []
  } }))
  assert.doesNotMatch(html, /data-status|cycleStatusTooltip/)
})

test('dashboard word statistics do not present a chapter writing-state breakdown', () => {
  const { DashboardWordStatistics } = component('views/dashboard/DashboardStatistics.tsx', {
    './BookAnalyticsCard': { BookAnalyticsCard: () => null }
  })
  const html = renderToStaticMarkup(createElement(DashboardWordStatistics, { t, statusTotal: 1, data: {
    averageChapterWords: 120, readingTimeMinutes: 2, statusBreakdown: [{ status: 'Outline', count: 1, wordCount: 120 }],
    outlineCount: 1, firstDraftCount: 0, revisedCount: 0, editedCount: 0, finalCount: 0
  } }))
  assert.doesNotMatch(html, /progressBreakdown|statusOutline/)
  assert.match(html, /dashboard.avgPerChapter/)
})
