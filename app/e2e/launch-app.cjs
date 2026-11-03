// Playwright's Electron preload forces password-store=basic before this entry.
// Restore Linux's real keyring selection before loading the app or reaching
// Electron readiness. CI provides an isolated, unlocked GNOME keyring session.
const { app } = require('electron')
if (process.platform === 'linux') app.commandLine.removeSwitch('password-store')
require('../out/main/index.js')
