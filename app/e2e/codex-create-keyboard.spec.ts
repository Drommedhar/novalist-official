import { expect, test } from '@playwright/test'
import { build } from 'esbuild'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// Use the actual dialog and modal focus restoration in headless Chromium.
// The surrounding Codex stores and name generator do not affect submission.
async function dialogFixture(): Promise<string> {
  const source = readFileSync(resolve('src/renderer/src/views/codex/CodexView.tsx'), 'utf8')
  const start = source.indexOf('function CodexCreateDialog(')
  if (start < 0) throw new Error('Codex creation dialog was not found')
  const result = await build({
    stdin: { resolveDir: process.cwd(), loader: 'tsx', contents: `
      import React, { useState } from 'react'
      import { createRoot } from 'react-dom/client'
      import { MotionPresence } from './src/renderer/src/shell/MotionPresence'
      import { installModalFocus } from './src/renderer/src/shell/modalFocus'
      import { dismissOnAndroidBack } from './src/renderer/src/mobile/backEvents'
      const NameSuggestions = () => null
      ${source.slice(start)}
      function Fixture() {
        const [pending, setPending] = useState(null)
        const [created, setCreated] = useState([])
        return <>
          <button onClick={() => setPending({ kind: 'create' })}>New entry</button>
          <output>{created.join(', ')}</output>
          <MotionPresence>{pending && <CodexCreateDialog
            setPending={setPending} t={key => key} startCreate={async name => setCreated(value => [...value, name])}
            templates={[]} templateId="" setTemplateId={() => {}} useWizard={false} setUseWizard={() => {}}
          />}</MotionPresence>
        </>
      }
      document.documentElement.dataset.shell = 'desktop'
      installModalFocus()
      createRoot(document.getElementById('root')).render(<Fixture />)
    ` },
    bundle: true, write: false, platform: 'browser', format: 'iife', jsx: 'automatic',
    loader: { '.css': 'empty' }, define: { 'process.env.NODE_ENV': '"test"' }
  })
  return result.outputFiles[0].text
}

test('Enter creates one Codex entry, closes its dialog and restores the opener', async ({ page }) => {
  test.setTimeout(30_000)
  await page.setContent('<div id="root"></div>')
  await page.addScriptTag({ content: await dialogFixture() })
  const opener = page.getByRole('button', { name: 'New entry', exact: true })
  await opener.click()
  await page.locator('#codex-create-name').fill('Nordwacht')
  await page.locator('#codex-create-name').press('Enter')
  await expect(page.locator('output')).toHaveText('Nordwacht')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(opener).toBeFocused()
  // Restoring focus must still allow a deliberate second keyboard activation.
  await opener.press('Enter')
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(page.locator('#codex-create-name')).toHaveValue('')
  await expect(page.locator('output')).toHaveText('Nordwacht')
})
