void (async () => {
  const config = window.mobileSmokeConfig
  const wait = async (predicate, description) => {
    window.mobileSmokeStage = description
    for (let count = 0; count < 240; count++) {
      if (await predicate()) return
      await new Promise(resolve => setTimeout(resolve, 250))
    }
    throw new Error('Timed out: ' + description)
  }
  const rpc = (method, params = []) => { window.mobileSmokeStage = method; return window.novalistRpc.request(method, params) }
  await rpc('collaboration/end')
  await rpc('project/close')
  const click = selector => {
    const element = document.querySelector(selector)
    if (!element) throw new Error('Missing control: ' + selector)
    element.click()
  }
  const input = (selector, value) => {
    const element = document.querySelector(selector)
    const prototype = element.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
    Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, value)
    element.dispatchEvent(new Event('input', { bubbles: true }))
  }
  click('[data-collaboration-trigger]')
  await wait(() => document.querySelector('#set-collaboration-mode'), 'collaboration menu')
  const modes = [...document.querySelectorAll('#set-collaboration-mode option')].map(option => option.value)
  if (modes.includes('steam')) throw new Error('Steam was offered on mobile')
  if (config.mode === 'hosted') {
    input('#set-collaboration-server', config.server)
    click('.collaboration-settings > .settings-button-row .primary')
    await wait(async () => (await rpc('collaboration/account/status', [config.server]))?.displayName, 'restored mobile sign-in')
    await wait(() => document.querySelector('#set-collaboration-join'), 'relay join controls')
    input('#set-collaboration-join', config.invitation)
    input('#set-collaboration-destination-name', 'Joined-' + Date.now())
  } else {
    const select = document.querySelector('#set-collaboration-mode')
    select.value = 'tcp'
    select.dispatchEvent(new Event('change', { bubbles: true }))
    await wait(() => document.querySelector('#set-peer-name'), 'direct session controls')
    input('#set-peer-name', 'Mobile writer')
    input('#set-peer-link', config.invitation)
    input('#set-peer-destination-name', 'Joined-' + Date.now())
  }
  await wait(() => !document.querySelector('.collaboration-options .settings-button-row .primary')?.disabled, 'default local folder')
  click('.collaboration-options .settings-button-row .primary')
  await wait(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy, 'initial project sync')
  const session = await rpc('collaboration/status')
  if (session.connection !== 'Connected') throw new Error('Mobile session is not connected')
  click('[data-collaboration-trigger]')
  const project = window.novalistStores.project
  window.novalistStores.shell.getState().setTourOpen(false)
  await new Promise(resolve => setTimeout(resolve, 300))
  window.novalistStores.shell.getState().setMobileTab('manuscript')
  window.novalistStores.shell.getState().setMainView('write')
  window.mobileSmokeStage = 'openScene'
  await project.getState().openScene(config.chapter, config.scene)
  await wait(() => document.querySelector('.editor-frame')?.contentDocument?.querySelector('#editor')?.textContent.includes('Desktop formatting.'), 'shared scene')
  const editor = document.querySelector('.editor-frame').contentDocument
  const content = editor.querySelector('#editor')
  content.focus()
  const selection = editor.getSelection()
  selection.selectAllChildren(content)
  selection.collapseToEnd()
  editor.execCommand('insertText', false, ' Mobile edit.')
  await wait(() => content.textContent.includes('Desktop reply.'), 'desktop reply')
  if (!content.querySelector('strong')) throw new Error('Formatting was lost')
  await rpc('collaboration/end')
  const saved = await rpc('scenes/read', [config.chapter, config.scene])
  if (!saved.html.includes('Mobile edit.') || !saved.html.includes('Desktop reply.')) throw new Error('Local copy is missing edits')
  await wait(() => document.querySelector('.editor-frame')?.contentDocument?.querySelector('#editor'), 'local editor after leaving')
  const local = document.querySelector('.editor-frame').contentDocument
  const localEditor = local.querySelector('#editor')
  localEditor.focus()
  local.getSelection().selectAllChildren(localEditor)
  local.getSelection().collapseToEnd()
  local.execCommand('insertText', false, ' Local copy edit.')
  await wait(async () => (await rpc('scenes/read', [config.chapter, config.scene])).html.includes('Local copy edit.'), 'local save after leaving')
  window.mobileSmokeResult = { ok: true, modes, html: saved.html, localCopy: project.getState().projectPath }
})().catch(error => { window.mobileSmokeResult = { ok: false, error: String(error), stack: error.stack,
  text: document.body.innerText.slice(-3000) } })
