import type { Page } from '@playwright/test'

/**
 * The first evaluate against a renderer that has only just loaded.
 *
 * Within roughly the first second after the page's `load`, a `page.evaluate`
 * that does real work - creating a project is the one that shows it - can fail
 * with "Execution context was destroyed, most likely because of a navigation".
 * It is intermittent, around one run in six locally, and it fails the setup step
 * of whichever spec happens to lose the race rather than anything the spec is
 * testing.
 *
 * What was ruled out, by instrumenting the page: there is no second navigation
 * (`framenavigated` never fires again), no reload (`load` and `domcontentloaded`
 * fire exactly once), no crash, no page error, no console error, and no frame
 * attach or detach - the page holds at one frame throughout. Waiting for the
 * window to be shown does not help, and neither does waiting for the start
 * screen to render: gated on `.start-screen` it still failed 4 runs in 15. Only
 * elapsed time helps, and a one-second pause made it 0 in 15.
 *
 * So there is no signal to wait for, only a window to get past. Rather than pad
 * every spec with a sleep, this retries - and only on that one error, so a
 * genuine failure inside the callback still fails on the first attempt.
 */
export async function evaluateWhenReady<Arg, Result>(
  page: Page,
  fn: (arg: Arg) => Result | Promise<Result>,
  arg?: Arg
): Promise<Result> {
  let last: unknown
  for (let attempt = 0; attempt < 4; attempt += 1) {
    try {
      // Playwright unboxes handles out of the argument type, which a plain
      // generic passthrough cannot express. Callers keep their own types; only
      // this hop is loose.
      return (await page.evaluate(
        fn as unknown as (a: unknown) => Result, arg as unknown
      )) as Result
    } catch (error) {
      if (!String(error).includes('Execution context was destroyed')) throw error
      last = error
      await page.waitForTimeout(250)
    }
  }
  throw last
}
