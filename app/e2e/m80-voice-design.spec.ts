import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdirSync, mkdtempSync, writeFileSync, copyFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * A character given a voice of their own, through an extension.
 *
 * Every piece of this is unit-tested against a stub and none of those tests can
 * say whether a writer can reach it: the hook is in the SDK, the brief is built
 * in Core, the design is an RPC, and the cast rail is the only thing that joins
 * them. The Settings override switch shipped broken with Core at 100% for
 * exactly this reason.
 *
 * The engine here is the sample extension's, which speaks no words - it makes
 * tones. That is the point of the seam: the whole path from a Codex entry to a
 * stored voice runs on a CI machine with no model, no download and no GPU.
 */
test('a character is given a designed voice from their Codex entry', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-voicedesign-'))
  const settingsDir = join(workDir, 'settings')
  const extensionDir = join(settingsDir, 'Extensions', 'Toolkit')
  mkdirSync(extensionDir, { recursive: true })

  const candidates = [
    join(process.cwd(), '..', 'Novalist.Sdk.Example', 'bin', 'Debug', 'net8.0', 'Novalist.Sdk.Example.dll'),
    join(process.cwd(), 'out', 'backend', 'Novalist.Sdk.Example.dll')
  ]
  const assembly = candidates.find((c) => existsSync(c)) ?? candidates[0]
  test.skip(!existsSync(assembly), 'sample extension assembly not built')
  copyFileSync(assembly, join(extensionDir, 'Novalist.Sdk.Example.dll'))
  writeFileSync(
    join(extensionDir, 'extension.json'),
    JSON.stringify({
      id: 'com.novalist.writingtoolkit',
      name: 'Writing Toolkit',
      version: '1.0.0',
      entryAssembly: 'Novalist.Sdk.Example.dll'
    })
  )

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = settingsDir
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(
    page,
    async (parent) => {
      const rpc = window.novalistRpc
      let state = await rpc.request('project/create', [parent, 'Voices', 'Book One'])
      const mira = (await rpc.request('entities/create', ['character', 'Mira'])) as { id: string }
      // The fields a brief is built from: fixed facts about how she sounds.
      await rpc.request('entities/update', [
        'character',
        mira.id,
        { age: '34', build: 'wiry' }
      ])

      state = await rpc.request('project/createChapter', ['One'])
      const chapters = (state as { chapters: { guid: string }[] }).chapters
      const guid = chapters[chapters.length - 1].guid
      state = await rpc.request('project/createScene', [guid, 'S'])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      await rpc.request('scenes/write', [
        guid,
        scenes[scenes.length - 1].id,
        '<p>"You are late," said Mira.</p>',
        'text'
      ])
      window.novalistStores.project.getState().applyState(state as never)
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await dismissTour(page)

  // Extensions load when the writer opens the Extensions view.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('extensions'))
  await page.waitForTimeout(3000)

  // The backend half first, so a failure below says which of the two broke.
  const engines = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('voiceEngines/list')) as {
        engineId: string
        isReady: boolean
      }[]
  )
  expect(engines.map((e) => e.engineId)).toContain('com.example.toolkit.voice')
  // Not ready at the moment it is first asked. Its model has never been loaded
  // in this process, and being honest about that before the wait is the point
  // of the field - a spinner after Play is pressed is not an answer.
  expect(engines[0].isReady).toBe(false)

  // But the asking is what starts it. This engine has nothing left to fetch, so
  // nobody has to press anything: the model lives in a process that ends with
  // the app, and a writer who prepared it yesterday should not be told it is
  // not ready again this morning. Only an engine with a download outstanding
  // waits to be asked, because that is a decision about their connection.
  await expect
    .poll(
      async () =>
        (
          await page.evaluate(
            async () =>
              (await window.novalistRpc.request('voiceEngines/list')) as { isReady: boolean }[]
          )
        )[0].isReady,
      { timeout: 20_000 }
    )
    .toBe(true)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))

  // So the rail has nothing left to prepare, and goes straight to the voices.
  // The narrator has one too, and theirs comes from the book rather than a
  // Codex entry - this is the character's.
  const design = page.locator('.narration-cast-row:not(.narrator) .narration-design').first()
  await expect(design).toBeVisible({ timeout: 20_000 })
  await design.click()

  // The brief is shown before anything is designed, built from the Codex entry,
  // and editable - a prompt assembled invisibly is one nobody can correct.
  const dialog = page.locator('.narration-design-dialog')
  await expect(dialog).toBeVisible({ timeout: 15_000 })
  await expect(dialog.locator('.narration-panel-where')).toContainText('Mira')
  const brief = dialog.locator('textarea')
  await expect(brief).toHaveValue(/Age: 34/)
  await expect(brief).toHaveValue(/wiry/)
  // Her own line came along, because how somebody talks describes their voice
  // better than any adjective.
  await expect(dialog.locator('.narration-design-samples li')).toContainText('You are late,')

  // An emotion typed into the brief must not survive: it would be baked into the
  // timbre for the whole book, which is what the per-line direction exists to
  // prevent.
  await brief.fill('Age: 34. Wiry and angry and joyful.')
  await dialog.locator('.narration-play').click()

  // Offered, not imposed. Design is not reliable per attempt - the same
  // description twice gives two voices, and one may not be the one asked for -
  // so it is played first and kept only if it is right.
  const candidate = dialog.locator('.narration-candidate audio')
  await expect(candidate).toBeVisible({ timeout: 60_000 })
  await expect(candidate).toHaveAttribute('src', /^novalist-audio:\/\/clip\//)

  // Nothing is stored until Keep.
  expect(
    await page.evaluate(
      async () => ((await window.novalistRpc.request('voiceEngines/voices')) as unknown[]).length
    )
  ).toBe(0)

  await dialog.getByRole('button', { name: /keep this voice|übernehmen|采用/i }).click()
  await expect(dialog).toHaveCount(0, { timeout: 30_000 })

  const designed = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('voiceEngines/voices')) as {
        voiceId: string
        displayName: string
        description: string
      }[]
  )
  const voice = designed[0]
  expect(designed).toHaveLength(1)
  expect(voice.displayName).toBe('Mira')
  expect(voice.description.toLowerCase()).toContain('wiry')
  expect(voice.description.toLowerCase()).not.toContain('angry')
  expect(voice.description.toLowerCase()).not.toContain('joyful')

  // Designed and cast, rather than designed and left one step short.
  const cast = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('narration/cast')) as {
        members: { name: string; voiceId: string | null }[]
      }
  )
  expect(cast.members[0].voiceId).toBe(voice.voiceId)

  // And the rail shows it as hers, in a picker that now offers designed voices
  // beside the machine's own.
  await expect(page.locator('.narration-voice').nth(1)).toHaveValue(voice.voiceId)

  // The audition reads one line at three points on the range - a single neutral
  // sample says nothing about whether the casting holds.
  const clips = await page.evaluate(
    async (id) =>
      (await window.novalistRpc.request('voiceEngines/audition', [id, 'You are late.'])) as {
        key: string
        audio: string
      }[],
    voice.voiceId
  )
  expect(clips.map((c) => c.key)).toEqual(['neutral', 'angry', 'sorrowful'])
  expect(clips.every((c) => c.audio.length > 0)).toBe(true)
  // Three readings of one identity, not three voices: same words, different
  // audio.
  expect(new Set(clips.map((c) => c.audio)).size).toBe(3)

  // ── The reading itself, performed by the engine ──

  // The narrator's voice comes from the book rather than from a Codex entry:
  // there is no entry to read, and what decides how a book is narrated is what
  // kind of book it is.
  const narrator = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('narration/designNarrator', [
        'com.example.toolkit.voice',
        'Level and unhurried.'
      ])) as { voiceId: string | null; error: string | null }
  )
  expect(narrator.error).toBeNull()
  // Kept, like any other designed voice: offering is the first half and casting
  // only happens on the second.
  expect(
    await page.evaluate(
      async () => (await window.novalistRpc.request('voiceEngines/keepVoice')) as boolean
    )
  ).toBe(true)

  // Now the whole reading goes through the engine rather than the machine's own
  // voices: every segment comes back as a clip in the cache.
  const render = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('narration/render', [0, 12, 1])) as {
        engineId: string | null
        clips: { key: string; clip: string | null; error: string | null }[]
        total: number
      }
  )
  expect(render.engineId).toBe('com.example.toolkit.voice')
  expect(render.total).toBeGreaterThan(0)
  expect(render.clips.length).toBe(render.total)
  expect(render.clips.every((c) => c.error === null && c.clip !== null)).toBe(true)
  // A name, not base64 in the message: audio does not belong in JSON.
  expect(render.clips[0].clip).toMatch(/^[0-9a-f]+\.[a-z0-9]+$/)

  // And the interface can actually fetch it, which is the half no unit test
  // reaches: the protocol handler, the cache folder and the name have to agree.
  const fetched = await page.evaluate(async (name) => {
    const response = await fetch(`novalist-audio://clip/${name}`)
    return { ok: response.ok, bytes: (await response.arrayBuffer()).byteLength }
  }, render.clips[0].clip)
  expect(fetched.ok).toBe(true)
  expect(fetched.bytes).toBeGreaterThan(44)

  // A name the cache could not have written gets no file back - refused by the
  // handler, or rejected before it even reaches one. Either is right; what
  // matters is that nothing outside the cache can be read through this.
  const traversal = await page.evaluate(async () => {
    try {
      const response = await fetch('novalist-audio://clip/..%2F..%2Fsettings.json')
      return { reached: true, ok: response.ok, status: response.status }
    } catch {
      return { reached: false, ok: false, status: 0 }
    }
  })
  expect(traversal.ok).toBe(false)

  const served = async (name: string): Promise<boolean> =>
    await page.evaluate(async (clip) => {
      try {
        const response = await fetch(`novalist-audio://clip/${clip}`)
        return response.ok
      } catch {
        // The file is gone, so the fetch does not resolve at all. That is the
        // point being made.
        return false
      }
    }, name)

  // Stopping keeps what was made. Stopping to fix a word and pressing Play
  // again is the commonest thing there is to do in this view, and it used to
  // cost the whole scene a second time.
  await page.evaluate(() => window.novalistRpc.request('narration/renderStop'))
  expect(await served(render.clips[0].clip!)).toBe(true)

  // Closing the project is where it goes. Speech of somebody's manuscript
  // should not outlive the project being open, and should certainly not still
  // be there behind whatever they open next.
  await page.evaluate(() => window.novalistRpc.request('project/close'))
  expect(await served(render.clips[0].clip!)).toBe(false)

  await app.close()
})
