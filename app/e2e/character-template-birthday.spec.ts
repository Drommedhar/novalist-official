import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('a birthday template creates an editable birth date and computes the scene age', async () => {
  const h = await launchApp('novalist-birthday-template-')
  try {
    const book = await seedBook(h, { Chapter: ['Arrival'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('templates'))
    const templates = h.page.locator('.templates-card')
    await templates.locator('.template-group').first().locator('.binder-rail-item').click()
    const dialog = h.page.locator('.type-manager-card')
    await dialog.locator('input.dialog-input').first().fill('Birthday template')
    const age = dialog.locator('.type-manager-field').filter({ has: h.page.getByRole('checkbox', { name: 'Age', exact: true }) })
    await age.getByRole('checkbox').check()
    await age.locator('select').first().selectOption('date')
    await age.locator('select').nth(1).selectOption('Months')
    await dialog.locator('.dialog-actions .primary').click()
    await expect(templates.locator('.type-manager-row', { hasText: 'Birthday template' })).toBeVisible()
    const list = await h.rpc<{ id: string; name: string }[]>('templates/list', ['character'])
    const template = list.find((row) => row.name === 'Birthday template')!

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    const character = await h.rpc<{ id: string; ageMode: string; ageIntervalUnit: string }>(
      'entities/create', ['character', 'Mira', template.id]
    )
    expect(character.ageMode).toBe('date')
    expect(character.ageIntervalUnit).toBe('Months')
    await h.page.evaluate(async (id) => {
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, character.id)
    const birthday = h.page.getByLabel('Birth date', { exact: true })
    await expect(birthday).toHaveAttribute('type', 'date')
    await expect(birthday).toHaveValue('')
    await birthday.fill('2001-05-13')
    // Tab moves between the native date input's segments on Windows; click
    // another field to commit the completed date by leaving the control.
    await h.page.getByRole('textbox', { name: 'Name', exact: true }).click()
    await expect.poll(async () => (await h.rpc<{ birthDate: string }>(
      'entities/get', ['character', character.id]
    )).birthDate).toBe('2001-05-13')

    const chapter = book.chapters[0]
    await h.rpc('calendar/reschedule', [chapter.guid, chapter.scenes[0].id, '2003-05-13'])
    const peek = await h.rpc<{ pills: { labelKey: string; arg: string }[] }>(
      'entities/peek', ['character', character.id, chapter.guid, chapter.title, chapter.scenes[0].title]
    )
    expect(peek.pills).toContainEqual(expect.objectContaining({ labelKey: 'focusPeek.agePill', arg: '24' }))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await expect(birthday).toHaveValue('2001-05-13')
  } finally {
    await h.close()
  }
})
