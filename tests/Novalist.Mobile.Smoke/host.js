void (async () => {
  const rpc = (method, params = []) => window.novalistRpc.request(method, params)
  await rpc('collaboration/end')
  await rpc('project/close')
  const root = await window.novalist.defaultProjectRoot()
  await rpc('project/create', [root + '/MobileHost-' + Date.now(), 'Mobile host', 'Book'])
  const book = await rpc('project/createChapter', ['Together'])
  const chapter = book.chapters.at(-1).guid
  const state = await rpc('project/createScene', [chapter, 'Mobile scene'])
  const scene = state.chapters.find(value => value.guid === chapter).scenes.at(-1).id
  await rpc('scenes/write', [chapter, scene, '<p>Hosted on mobile.</p>', 'Hosted on mobile.'])
  await rpc('collaboration/peer/host', ['127.0.0.1', 42685, 'Mobile host'])
  const invitation = await rpc('collaboration/invite', [new Date(Date.now() + 600_000).toISOString()])
  window.mobileSmokeResult = { ok: true, hosting: true, invitation, chapter, scene }
})().catch(error => { window.mobileSmokeResult = { ok: false, error: String(error), stack: error.stack } })
