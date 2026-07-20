import { BrowserWindow } from 'electron'
import { readFileSync } from 'node:fs'

/**
 * A small frameless splash window shown while the backend spins up and the
 * update check runs, mirroring the Avalonia startup splash. It loads a
 * self-contained data: URL (no renderer/vite entry) and closes once the main
 * window is ready. Status text is updated from the main process.
 */
export function createSplashWindow(iconPath: string | null): BrowserWindow {
  const win = new BrowserWindow({
    width: 440,
    height: 300,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    resizable: false,
    movable: false,
    center: true,
    show: false,
    alwaysOnTop: true,
    skipTaskbar: true,
    webPreferences: { contextIsolation: true, nodeIntegration: false }
  })

  let iconTag = ''
  if (iconPath) {
    try {
      const b64 = readFileSync(iconPath).toString('base64')
      iconTag = `<img class="logo" src="data:image/png;base64,${b64}" width="72" height="72" alt="" />`
    } catch {
      // The icon is optional; the splash still shows the name.
    }
  }

  const html = `<!doctype html><html><head><meta charset="utf-8"><style>
    html,body{margin:0;height:100%;background:transparent;
      font-family:-apple-system,'Segoe UI',system-ui,sans-serif;}
    .card{box-sizing:border-box;height:100%;display:flex;flex-direction:column;
      align-items:center;justify-content:center;gap:16px;padding:28px;
      background:#1b1b1c;color:#e8e8ea;border:1px solid #2c2c2e;border-radius:16px;}
    .logo{border-radius:14px}
    .name{font-size:22px;font-weight:600;letter-spacing:.4px}
    .bar{width:190px;height:3px;border-radius:3px;background:#2c2c2e;overflow:hidden;position:relative}
    .bar::after{content:'';position:absolute;left:-40%;top:0;height:100%;width:40%;border-radius:3px;
      background:#4f8cff;animation:slide 1.1s infinite ease-in-out}
    @keyframes slide{0%{left:-40%}100%{left:110%}}
    .status{font-size:13px;color:#9a9a9c;min-height:18px;text-align:center}
  </style></head><body><div class="card">${iconTag}
    <div class="name">Novalist</div>
    <div class="bar"></div>
    <div class="status" id="s">Starting Novalist…</div>
  </div>
  <script>window.setStatus=function(t){var e=document.getElementById('s');if(e)e.textContent=t}</script>
  </body></html>`

  void win.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html))
  win.once('ready-to-show', () => win.show())
  return win
}

/** Updates the splash status line, ignoring a splash that has already closed. */
export function setSplashStatus(win: BrowserWindow | null, text: string): void {
  if (!win || win.isDestroyed()) return
  void win.webContents.executeJavaScript(
    `window.setStatus && window.setStatus(${JSON.stringify(text)})`
  )
}
