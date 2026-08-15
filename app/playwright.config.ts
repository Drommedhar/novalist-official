import { defineConfig } from '@playwright/test'

/**
 * The system clipboard is off limits for the whole run.
 *
 * Every spec builds its Electron environment from `process.env`, whether it
 * goes through the harness or launches the app itself, so setting this here is
 * the one place that covers all of them. See the handler in main/dialogs.ts for
 * what it costs to get this wrong.
 */
process.env.NOVALIST_NO_CLIPBOARD = '1'

export default defineConfig({
  testDir: './e2e',
  timeout: 180_000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    trace: 'retain-on-failure'
  }
})
