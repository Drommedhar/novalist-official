import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const projects = ['Novalist.Mobile', 'Novalist.Backend', 'Novalist.Core', 'Novalist.Sdk', 'Novalist.Sync.Client', 'native/AppleSpeech']
const extensions = new Set(['.cs', '.csproj', '.props', '.targets', '.swift', '.sh', '.plist', '.png'])
const files = execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(file =>
  projects.some(project => file.startsWith(project + '/')) &&
  (extensions.has(path.extname(file)) || file.startsWith('Novalist.Core/Resources/Analysis/') || file === 'Novalist.Sdk/README.md'))
files.push('Directory.Build.props',
  'Novalist.Backend/ICollaborationHost.cs',
  'Novalist.Sync.Client/IHostedSignInBrowser.cs',
  'Novalist.Mobile/Pages/RendererHostPage.Collaboration.cs',
  'Novalist.Mobile/Services/MobileCollaborationHost.cs',
  'Novalist.Mobile/Services/MobileDocumentRequests.cs',
  'native/Sodium/Sodium.iOS.targets',
  'native/Sodium/build-simulator.sh',
  'tests/Novalist.Mobile.Smoke/Smoke.targets',
  'tests/Novalist.Mobile.Smoke/SmokeApp.cs')
function assets(directory) {
  for (const entry of readdirSync(path.join(root, directory), { withFileTypes: true })) {
    const relative = directory + '/' + entry.name
    if (entry.isDirectory()) assets(relative)
    else if (entry.isFile()) files.push(relative)
  }
}
assets('Novalist.Mobile/Resources/Raw/app')
assets('app/src/renderer/public/editor')
const manifest = [...new Set(files)].sort()
for (const file of manifest) if (!existsSync(path.join(root, file))) throw new Error('Missing build input: ' + file)
const output = path.join(root, 'app/out')
mkdirSync(output, { recursive: true })
writeFileSync(path.join(output, 'mobile-source-manifest.txt'), manifest.join('\n') + '\n')
execFileSync('tar', ['-czf', path.join(output, 'mobile-collaboration-source.tar.gz'), '-T', path.join(output, 'mobile-source-manifest.txt')], { cwd: root })
process.stdout.write(`Packaged ${manifest.length} build inputs. Manifest: app/out/mobile-source-manifest.txt\n`)
