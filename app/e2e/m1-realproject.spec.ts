import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * Opens a copy of a real Novalist project (119 scenes, German) and verifies
 * the binder and editor render real content. The original is never touched;
 * the app works on a temp copy.
 */

test('real project renders binder and scene content', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-real-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  // Real chapters and scenes appear in the binder.
  const chapterRows = page.locator('.binder-chapter-row')
  await expect.poll(() => chapterRows.count()).toBeGreaterThan(3)
  const sceneRows = page.locator('.binder-scene-row')
  await expect.poll(() => sceneRows.count()).toBeGreaterThan(10)

  // Open the first scene; the real editor shows real prose.
  await sceneRows.first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => ((await editor.innerText()) ?? '').trim().length, { timeout: 15_000 })
    .toBeGreaterThan(50)

  // Focus peek: hovering a character's first name in the prose ("Liam" for the
  // character "Liam Calder") shows the entity peek card. Regression: the peek
  // index only carried composed full names, so bare first names never matched.
  await page.evaluate(async () => {
    const st = window.novalistStores.project.getState()
    for (const ch of st.chapters)
      for (const scn of ch.scenes) {
        const c = (await window.novalistRpc.request('scenes/read', [ch.guid, scn.id])) as {
          html: string
        }
        if (c.html.includes('Liam')) {
          await st.openScene(ch.guid, scn.id)
          return
        }
      }
  })
  const peekEditor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(peekEditor).toBeVisible({ timeout: 15_000 })
  await page.waitForTimeout(1500)
  const peekBox = await page.locator('.editor-frame').boundingBox()
  const peekFrame = page.frames().find((f) => f.url().includes('editor'))
  const liam =
    peekFrame &&
    (await peekFrame.evaluate(() => {
      const w = document.createTreeWalker(document.getElementById('editor')!, NodeFilter.SHOW_TEXT)
      let n: Node | null
      while ((n = w.nextNode())) {
        const i = (n.textContent ?? '').indexOf('Liam')
        if (i >= 0) {
          const r = document.createRange()
          r.setStart(n, i)
          r.setEnd(n, i + 4)
          ;(r.startContainer.parentElement as HTMLElement).scrollIntoView({ block: 'center' })
          const b = r.getBoundingClientRect()
          return { x: b.left + b.width / 2, y: b.top + b.height / 2 }
        }
      }
      return null
    }))
  if (liam && peekBox) {
    await page.mouse.move(peekBox.x + liam.x - 8, peekBox.y + liam.y)
    await page.mouse.move(peekBox.x + liam.x, peekBox.y + liam.y)
    await expect(page.locator('.peek-card')).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.peek-title')).toContainText('Liam')
  }

  // Inspector context panel: scene analysis (POV + stats) computed for the
  // open scene, and any characters detected in the prose are listed.
  await expect(page.locator('.ctx-panel')).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.ctx-stats')).toBeVisible()
  await expect(page.locator('.ctx-pov')).toBeVisible()

  // Sidebar focus-peek: hovering an entity card in the Inspector shows the very
  // same peek card as the editor. Move the pointer off the prose first so the
  // editor's own peek dismisses, then hover a character card and assert the peek.
  await page.mouse.move(4, 4)
  await expect(page.locator('.peek-card')).toHaveCount(0, { timeout: 5_000 })
  const ctxCard = page.locator('.ctx-card').first()
  if (await ctxCard.count()) {
    await ctxCard.hover()
    await expect(page.locator('.peek-card')).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.peek-title')).toBeVisible()
  }

  // Scene-notes dock: open it, write a synopsis, blur, confirm it persisted.
  await page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
  const synopsis = page.locator('#dock-synopsis')
  await expect(synopsis).toBeVisible({ timeout: 10_000 })
  await synopsis.fill('Verification synopsis from e2e')
  await synopsis.blur()
  await expect
    .poll(async () => {
      return page.evaluate(async () => {
        const store = window.novalistStores.project.getState()
        const meta = (await window.novalistRpc.request('scenes/getMeta', [
          store.openChapterGuid,
          store.openSceneId
        ])) as { synopsis: string | null }
        return meta.synopsis
      })
    })
    .toBe('Verification synopsis from e2e')

  // Codex: real characters render in the list.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await expect.poll(() => page.locator('.codex-row').count(), { timeout: 15_000 }).toBeGreaterThan(0)

  // Book-local entity images must actually load (regression: paths were resolved
  // against the project root, missing the book folder → 404). Assert a real
  // portrait thumbnail decodes to non-zero pixels.
  const thumb = page.locator('img.codex-thumb').first()
  await expect(thumb).toBeVisible({ timeout: 15_000 })
  await expect
    .poll(() => thumb.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth), {
      timeout: 15_000
    })
    .toBeGreaterThan(0)

  // Entity navigation: characters group into role/group sections with headers.
  await expect
    .poll(() => page.locator('.codex-group-head').count(), { timeout: 10_000 })
    .toBeGreaterThan(0)

  // Typed detail pane: selecting a character shows grouped sections
  // (Basic Info + Physical Attributes), not a raw key dump.
  await page.locator('.codex-row').first().click()
  await expect
    .poll(() => page.locator('.codex-field-section').count(), { timeout: 10_000 })
    .toBeGreaterThanOrEqual(2)

  // Search filters the navigation list.
  await page.locator('.codex-search').fill('zzz-no-such-entity')
  await expect.poll(() => page.locator('.codex-row').count(), { timeout: 5_000 }).toBe(0)
  await page.locator('.codex-search').fill('')
  await expect.poll(() => page.locator('.codex-row').count(), { timeout: 5_000 }).toBeGreaterThan(0)

  // Locations render as a parent/child tree (real project has nested locations).
  await page.evaluate(() => window.novalistStores.codex.getState().setType('location'))
  await expect.poll(() => page.locator('.codex-row').count(), { timeout: 10_000 }).toBeGreaterThan(0)
  const indented = await page.evaluate(() =>
    Array.from(document.querySelectorAll<HTMLElement>('.codex-row')).some(
      (el) => (el.style.paddingLeft ?? '') !== ''
    )
  )
  expect(indented).toBe(true)
  await page.evaluate(() => window.novalistStores.codex.getState().setType('character'))

  // Custom entity types: create a type through the manager dialog, then an
  // entity of that type through its new codex tab.
  await page.locator('.codex-tab-manage').click()
  await page.locator('.type-manager-card .dialog-button.primary').click()
  await page.locator('.type-manager-card .dialog-input').first().fill('Faction')
  await page.locator('.type-manager-card .dialog-actions .dialog-button.primary').click()
  await expect(page.locator('.type-manager-row', { hasText: 'Faction' })).toBeVisible({
    timeout: 10_000
  })
  await page.locator('.type-manager-card .binder-expand').first().click()
  await page.locator('.codex-tab', { hasText: 'Factions' }).click()
  await page.locator('.codex-list .binder-rail-item').click()
  await page.locator('#codex-create-name').fill('Nordwacht')
  await page.keyboard.press('Enter')
  await expect(page.locator('.codex-row', { hasText: 'Nordwacht' })).toBeVisible({
    timeout: 10_000
  })

  // Guided creation wizard: create a character with the wizard checkbox on,
  // answer the surname step, skip the rest, and verify the answer persisted.
  await page.locator('.codex-tab').first().click()
  await page.locator('.codex-list .binder-rail-item').click()
  await page.locator('#codex-create-name').fill('Wizardborn')
  await page.locator('.dialog-card .type-manager-check input').check()
  await page.locator('.dialog-card .dialog-actions .dialog-button.primary').click()
  const wizardCard = page.locator('.dialog-card', { hasText: '1/6' })
  await expect(wizardCard).toBeVisible({ timeout: 10_000 })
  await page.locator('.dialog-card .dialog-input').fill('Frostmantel')
  await page.locator('.dialog-card .dialog-button.primary').click()
  for (let i = 0; i < 4; i += 1) {
    await page.locator('.dialog-card .dialog-button', { hasText: /^(Skip|Überspringen)$/ }).click()
  }
  // The wizard's long-answer step is a live-styled Markdown editor now, so its
  // writing surface is a contenteditable rather than a textarea: type into it
  // instead of filling, and confirm the text actually landed.
  const wizardAnswer = page.locator('.dialog-card .md-editor .cm-content')
  await expect(wizardAnswer).toBeVisible({ timeout: 10_000 })
  await wizardAnswer.click()
  await page.keyboard.type('Born in the e2e harness.')
  await expect(wizardAnswer).toContainText('Born in the e2e harness.')
  await page.locator('.dialog-card .dialog-button.primary').click()
  await page.locator('.codex-row', { hasText: 'Wizardborn' }).click()
  await expect
    .poll(async () => {
      return page.evaluate(() => {
        const record = window.novalistStores.codex.getState().selectedRecord
        return record ? (record as { surname?: string }).surname : null
      })
    }, { timeout: 10_000 })
    .toBe('Frostmantel')

  // Template editor: create a character template in Settings, verify it lists.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
  const templatesCard = page.locator('.templates-card')
  await expect(templatesCard).toBeVisible({ timeout: 15_000 })
  await templatesCard.locator('.template-group').first().locator('.binder-rail-item').click()
  const templateDialog = page.locator('.dialog-overlay .dialog-card')
  await expect(templateDialog).toBeVisible({ timeout: 10_000 })
  await templateDialog.locator('input.dialog-input').first().fill('E2E Template')
  await templateDialog.locator('.dialog-actions .dialog-button.primary').click()
  await expect(
    templatesCard.locator('.type-manager-row', { hasText: 'E2E Template' })
  ).toBeVisible({ timeout: 10_000 })

  // Dashboard: real totals appear.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await expect(page.locator('.dashboard-title')).toBeVisible({ timeout: 15_000 })
  const wordsMetric = await page.locator('.dashboard-metric-value').first().innerText()
  expect(Number(wordsMetric.replace(/[^0-9]/g, ''))).toBeGreaterThan(1000)

  // Manuscript: corkboard cards and outliner rows render from real scenes.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
  await expect(page.locator('.editor-frame')).toBeVisible({ timeout: 15_000 })
  await page.locator('.manuscript-modes button').nth(1).click()
  await expect.poll(() => page.locator('.corkboard-card').count(), { timeout: 15_000 }).toBeGreaterThan(5)
  await page.locator('.manuscript-modes button').nth(2).click()
  await expect.poll(() => page.locator('.outliner-row').count(), { timeout: 15_000 }).toBeGreaterThan(5)

  // Timeline: groups render from real chapter/scene dates (or the empty hint).
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await expect(page.locator('.timeline-toolbar')).toBeVisible({ timeout: 15_000 })

  // Structure templates: applying Save the Cat appends its 15 manual beats.
  const beforeManual = await page.locator('.timeline-event.source-manual').count()
  await page
    .locator('.timeline-toolbar select')
    .first()
    .selectOption('save-the-cat')
  await expect
    .poll(() => page.locator('.timeline-event.source-manual').count(), { timeout: 10_000 })
    .toBe(beforeManual + 15)

  // Plot grid: table or empty hint renders without error.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
  await expect(page.locator('.plotgrid-toolbar')).toBeVisible({ timeout: 15_000 })

  // Calendar: toolbar with mode buttons renders.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
  await expect(page.locator('.calendar .timeline-toolbar')).toBeVisible({ timeout: 15_000 })

  // Hotkeys + command palette: Ctrl+Shift+P opens the palette; running the
  // Timeline command switches the main view.
  await page.keyboard.press('ControlOrMeta+Shift+P')
  await expect(page.locator('.palette-card')).toBeVisible({ timeout: 10_000 })
  await page.keyboard.type('Timeline')
  await page.keyboard.press('Enter')
  await expect(page.locator('.timeline-toolbar')).toBeVisible({ timeout: 10_000 })

  // Relationships: real characters produce graph nodes.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('relationships'))
  await expect
    .poll(() => page.locator('.relationships-node').count(), { timeout: 15_000 })
    .toBeGreaterThan(0)

  // SDK v2: the deployed AiAssistant contributes a webview panel; opening it
  // exercises manifest discovery, the novalist-ext protocol, and the
  // postMessage-to-controller bridge end to end.
  const aiChatItem = page.locator('.activity-bar-item[title="AI Chat"]')
  if ((await aiChatItem.count()) > 0) {
    await aiChatItem.click()
    const chatFrame = page.frameLocator('iframe[src^="novalist-ext://com.novalist.ai/"]')
    await expect(chatFrame.locator('#composer')).toBeVisible({ timeout: 15_000 })
  }

  await app.close()
})
