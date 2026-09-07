import type { JSHandle, Page } from '@playwright/test'

type Outcome<T> =
  | { ok: true; value: T }
  | { ok: false; message: string; stack?: string }

type Evaluation<Arg, Result> = ((arg: Arg) => Result | Promise<Result>) & {
  result?: Promise<Outcome<Result>>
}

/**
 * Electron can lose an evaluation reply shortly after the renderer loads,
 * reporting "Execution context was destroyed" even while the page survives.
 * Retrying the callback itself can repeat a completed write: a scene creation
 * lost this way left two copies of "Keep" in the archive regression.
 *
 * Keep the callback and its single result on a renderer handle. Transport
 * retries reuse that handle and await the original result. A real navigation
 * invalidates the handle and fails the call instead of replaying a mutation in
 * the new document.
 */
export async function evaluateWhenReady<Arg, Result>(
  page: Page,
  fn: (arg: Arg) => Result | Promise<Result>,
  arg?: Arg
): Promise<Result> {
  async function retryTransport<T>(operation: () => Promise<T>): Promise<T> {
    let last: unknown
    for (let attempt = 0; attempt < 4; attempt += 1) {
      try {
        return await operation()
      } catch (error) {
        if (!String(error).includes('Execution context was destroyed')) throw error
        last = error
        await page.waitForTimeout(250)
      }
    }
    throw last
  }

  // Obtain the function without calling it. Retrying this read cannot write to
  // the project, and subsequent calls stay bound to this execution context.
  const callback = await retryTransport(() =>
    page.evaluateHandle(`(${fn.toString()})`)
  ) as JSHandle<Evaluation<Arg, Result>>
  try {
    await retryTransport(() => callback.evaluate((run, value) => {
      // Store the promise before invoking the callback, so even a lost start
      // acknowledgement cannot schedule it twice. Capture rejection immediately
      // to avoid unhandled errors while the transport is recovering.
      // Playwright unboxes handle arguments; this helper's callers pass values.
      run.result ??= Promise.resolve().then(() => run(value as Arg)).then(
        (value) => ({ ok: true as const, value }),
        (error: unknown) => ({
          ok: false as const,
          message: error instanceof Error ? error.message : String(error),
          stack: error instanceof Error ? error.stack : undefined
        })
      )
    }, arg as Arg))
    const outcome = await retryTransport(() =>
      callback.evaluate(async (run) => await run.result!)
    )
    if (!outcome.ok) {
      const error = new Error(outcome.message)
      if (outcome.stack) error.stack = outcome.stack
      throw error
    }
    return outcome.value
  } finally {
    await callback.dispose()
  }
}
