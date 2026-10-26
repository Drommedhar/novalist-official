import { initializeMobileHost } from './shim'

void initializeMobileHost().then(async capabilities => {
  const { installNativeLayout } = await import('./nativeLayout')
  installNativeLayout(capabilities.layout)
  await import('../main')
}).catch(() => {
  const root = document.getElementById('root')
  if (!root) return
  const message = document.createElement('p')
  message.textContent = 'Novalist could not start. Close and reopen the app to retry.'
  message.setAttribute('role', 'alert')
  root.replaceChildren(message)
})
