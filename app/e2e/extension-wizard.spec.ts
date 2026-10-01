import { test, expect, type Page } from '@playwright/test'
import type { WizardSession, WizardStepDef } from '../src/renderer/src/stores/hostBridgeStore'
import { evaluateWhenReady } from './appReady'
import { dismissTour, launchApp, seedBook } from './harness'

function step(values: Partial<WizardStepDef> & Pick<WizardStepDef, 'id' | 'title'>): WizardStepDef {
  return {
    kind: 'text', help: null, skippable: true, visibleWhen: null, hasValidator: false,
    multiline: false, maxLength: null, placeholder: null, exampleValue: null, choices: null,
    multiSelect: false, hasDynamicChoices: false, autoSkipIfChoicesEmpty: false,
    min: null, max: null, defaultNumber: 0, unit: null, allowInWorld: false,
    targetEntityTypeKey: null, minCount: null, maxCount: null, subSteps: null,
    ...values
  }
}

// Seeded provider answers must remain hidden when only dictation is selected.
function aiSetup(provider: string, features: string): WizardSession {
  const whenEnabled = { stepId: 'features', operator: 'contains', value: 'ai' }
  const whenDictation = { stepId: 'features', operator: 'contains', value: 'dictation' }
  const whenProvider = (value: string) => ({ stepId: 'provider', operator: 'equals', value })
  return {
    token: 'ai-setup-test',
    definition: {
      id: 'extension.ai.setup', displayName: 'AI Assistant — setup', description: '',
      scope: 'Reference', entityTypeKey: null,
      steps: [
        step({ id: 'features', title: 'Which features would you like?', kind: 'choice', skippable: false,
          choices: [
            { value: 'ai', label: 'AI assistance only', description: null },
            { value: 'dictation', label: 'Local dictation only', description: null },
            { value: 'ai,dictation', label: 'Both', description: null },
            { value: 'none', label: 'Not now', description: null }
          ] }),
        step({ id: 'provider', title: 'Which provider?', kind: 'choice', skippable: false,
          visibleWhen: whenEnabled,
          choices: ['lmstudio', 'copilot', 'claude'].map((value) => ({ value, label: value, description: null })) }),
        step({ id: 'lmStudioBaseUrl', title: 'LM Studio base URL', hasValidator: true,
          visibleWhen: whenProvider('lmstudio') }),
        step({ id: 'lmStudioModel', title: 'Model', kind: 'choice', hasDynamicChoices: true,
          autoSkipIfChoicesEmpty: true, visibleWhen: whenProvider('lmstudio') }),
        step({ id: 'lmStudioApiToken', title: 'API token (optional)', visibleWhen: whenProvider('lmstudio') }),
        step({ id: 'copilotPath', title: 'Copilot CLI path', visibleWhen: whenProvider('copilot') }),
        step({ id: 'copilotModel', title: 'Copilot model (optional)', visibleWhen: whenProvider('copilot') }),
        step({ id: 'claudePath', title: 'Claude Code CLI path', visibleWhen: whenProvider('claude') }),
        step({ id: 'claudeModel', title: 'Claude model', visibleWhen: whenProvider('claude') }),
        step({ id: 'responseLanguage', title: 'Response language', visibleWhen: whenEnabled }),
        ...['dictationAcceleration', 'dictationModel', 'dictationDialogueModel', 'prepareDictation'].map((id) =>
          step({ id, title: id, visibleWhen: whenDictation }))
      ]
    },
    seed: {
      definitionId: 'extension.ai.setup', currentStepIndex: 0, completed: false,
      answers: {
        features: { text: features }, provider: { text: provider },
        lmStudioBaseUrl: { text: 'http://localhost:1234' },
        copilotPath: { text: 'copilot' }, claudePath: { text: 'claude' },
        claudeModel: { text: 'sonnet' }
      }
    }
  }
}

async function openWizard(page: Page, session: WizardSession): Promise<void> {
  await evaluateWhenReady(page, (wizard) => {
    // Deliver the same notification as the backend, without requiring the AI
    // extension or an actual model server on the machine running the test.
    ;(window.novalistRpc as unknown as { dispatch: (message: unknown) => void }).dispatch({
      method: 'ui/wizard/open', params: [wizard]
    })
  }, session)
}

test('Not now completes AI setup without visiting any seeded provider branch', async () => {
  const h = await launchApp('nl-wizard-disabled-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const calls = await h.page.evaluateHandle(() => {
      const rpc = window.novalistRpc
      const calls: { method: string; params: unknown }[] = []
      const request = rpc.request.bind(rpc)
      rpc.request = (method, params) => {
        if (method.startsWith('ui/wizard/')) calls.push({ method, params })
        return request(method, params)
      }
      const notify = rpc.notify.bind(rpc)
      rpc.notify = (method, params) => {
        if (method === 'ui/wizard/complete') calls.push({ method, params })
        else notify(method, params)
      }
      return calls
    })
    for (const provider of ['lmstudio', 'copilot', 'claude']) {
      for (const initialFeatures of ['none', 'ai', 'dictation', 'ai,dictation']) {
        await calls.evaluate((entries) => { entries.length = 0 })
        await openWizard(h.page, aiSetup(provider, initialFeatures))
        const dialog = h.page.getByRole('dialog', { name: 'AI Assistant — setup' })
        await dialog.getByRole('radio', { name: 'Not now', exact: true }).check()
        await expect(dialog.locator('.wizard-host-progress')).toHaveText('1/1')
        await dialog.locator('.dialog-button.primary').click()
        await expect(dialog).toHaveCount(0)
        // Completing (not cancelling) sends features=none so the extension
        // persists the opt-out; no choices or validation RPC should run.
        expect(await calls.jsonValue()).toEqual([{
          method: 'ui/wizard/complete',
          params: ['ai-setup-test', expect.objectContaining({
            completed: true, currentStepIndex: 0,
            answers: expect.objectContaining({ features: { text: 'none' } })
          })]
        }])
      }
    }
  } finally {
    await h.close()
  }
})

test('enabling AI restores provider choices and follows the newly selected branch', async () => {
  const h = await launchApp('nl-wizard-enabled-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await openWizard(h.page, aiSetup('lmstudio', 'none'))
    const dialog = h.page.getByRole('dialog', { name: 'AI Assistant — setup' })
    await dialog.getByRole('radio', { name: 'AI assistance only', exact: true }).check()
    await expect(dialog.locator('.wizard-host-progress')).toHaveText('1/6')
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog.getByRole('radio', { name: 'lmstudio', exact: true })).toBeChecked()
    await dialog.getByRole('radio', { name: 'claude', exact: true }).check()
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog.locator('.wizard-host-label')).toHaveText('Claude Code CLI path')
    await expect(dialog.getByRole('textbox')).toHaveValue('claude')
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog.locator('.wizard-host-label')).toHaveText('Claude model')
    await expect(dialog.getByRole('textbox')).toHaveValue('sonnet')
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog.locator('.wizard-host-label')).toHaveText('Response language')
    await expect(dialog.locator('.wizard-host-progress')).toHaveText('5/5')
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog).toHaveCount(0)
  } finally {
    await h.close()
  }
})

test('dictation-only skips the seeded provider and both exposes both feature branches', async () => {
  const h = await launchApp('nl-wizard-dictation-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await openWizard(h.page, aiSetup('claude', 'ai'))
    const dialog = h.page.getByRole('dialog', { name: 'AI Assistant — setup' })
    await dialog.getByRole('radio', { name: 'Both', exact: true }).check()
    await expect(dialog.locator('.wizard-host-progress')).toHaveText('1/9')
    await dialog.getByRole('radio', { name: 'Local dictation only', exact: true }).check()
    await expect(dialog.locator('.wizard-host-progress')).toHaveText('1/5')
    for (const title of ['dictationAcceleration', 'dictationModel', 'dictationDialogueModel', 'prepareDictation']) {
      await dialog.locator('.dialog-button.primary').click()
      await expect(dialog.locator('.wizard-host-label')).toHaveText(title)
    }
    await dialog.locator('.dialog-button.primary').click()
    await expect(dialog).toHaveCount(0)
  } finally { await h.close() }
})
