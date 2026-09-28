import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

/**
 * A field the writer defines has to reach the surfaces that use it.
 *
 * The backend can be perfect and the feature still absent: a definition that
 * no view reads is a column nobody has, which is exactly how a settings switch
 * once shipped wired to nothing.
 */
test('a scene field defined in Settings becomes an editable outliner column', async () => {
  test.setTimeout(120_000)

  const h = await launchApp('nl-fields-')
  try {
    const { page } = h
    const book = await seedBook(h, { 'Chapter One': ['Opening'] }, 'Fields')
    await dismissTour(page)
    const sceneId = book.chapters[0].scenes[0].id

    // ── Define a field in Settings, the way a writer would ──
    await page.evaluate(() =>
      window.novalistStores.shell.getState().openSettings('manuscriptProperties')
    )
    // The section is identified by its stable key rather than by its heading
    // text, which is translated, or by a card class it no longer carries.
    const card = page.locator('.settings-section-surface[data-settings-section="manuscriptProperties"]')
    await expect(card).toBeVisible({ timeout: 20_000 })

    await card.getByRole('button', { name: 'Add a field' }).click()
    const row = card.locator('.props-row').first()
    await row.locator('input').first().fill('Tension')
    await row.locator('select').nth(1).selectOption('Int')
    await row.getByRole('checkbox').check()
    await card.getByRole('button', { name: 'Save' }).click()

    // ── It becomes a column in the outliner, and typing in it sticks ──
    await page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await page.getByRole('button', { name: 'Outliner', exact: true }).click()

    await expect(page.locator('.outliner-head')).toContainText('Tension', { timeout: 20_000 })
    const cell = page.locator('.outliner-row:not(.outliner-head) input[aria-label="Tension"]')
    await expect(cell).toHaveCount(1)
    await cell.fill('8')
    await cell.blur()

    // Round-trips through the backend rather than only living in the input. The
    // key is generated and never shown, so it is read back rather than guessed.
    await expect
      .poll(
        async () =>
          (await page.evaluate(async (id) => {
            const defs = (await window.novalistRpc.request(
              'manuscriptProps/definitions'
            )) as { key: string }[]
            const all = (await window.novalistRpc.request('manuscriptProps/allSceneValues')) as Record<
              string,
              Record<string, string>
            >
            return all[id]?.[defs[0]?.key] ?? null
          }, sceneId)) as string | null,
        { timeout: 10_000 }
      )
      .toBe('8')

  } finally { await h.close() }
})
