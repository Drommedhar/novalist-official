import { spawnSync } from 'node:child_process'

const platforms = {
  win32: 'win',
  darwin: 'osx',
  linux: 'linux'
}
const architectures = {
  x64: 'x64',
  arm64: 'arm64'
}

const platform = platforms[process.platform]
const architecture = architectures[process.arch]
const rid = process.env.npm_config_rid ||
  (platform && architecture ? `${platform}-${architecture}` : '')

if (!rid) {
  console.error(
    `Cannot infer a .NET runtime identifier for ${process.platform}/${process.arch}. ` +
      'Set npm_config_rid explicitly.'
  )
  process.exit(1)
}

console.log(`Publishing Novalist.Backend for ${rid}`)
const result = spawnSync(
  'dotnet',
  [
    'publish',
    '../Novalist.Backend/Novalist.Backend.csproj',
    '-c',
    'Release',
    '-r',
    rid,
    '--self-contained',
    '-p:PublishSingleFile=true',
    '-o',
    'dist-backend'
  ],
  { cwd: new URL('..', import.meta.url), stdio: 'inherit', shell: false }
)

if (result.error) {
  console.error(result.error.message)
  process.exit(1)
}
process.exit(result.status ?? 1)
