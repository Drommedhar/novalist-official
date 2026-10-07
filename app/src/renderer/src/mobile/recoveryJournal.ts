export interface RecoveryScope {
  projectPath: string
  bookId: string
  draftId: string
}

export interface SceneRecovery {
  kind: 'scene'
  source: string
  chapterGuid: string
  sceneId: string
  html: string
  plainText: string
  hash: string
}

export interface ResearchRecoveryValue {
  id: string
  title: string
  type: string
  content: string
  tags: string[]
  entityRefs: string[]
}

export interface ResearchRecovery {
  kind: 'research'
  source: string
  draft: ResearchRecoveryValue
  base: ResearchRecoveryValue
}

export type RecoveryEntry = (SceneRecovery | ResearchRecovery) & { scope: RecoveryScope }
const STORAGE_KEY = 'novalist.mobile.recovery.v1'
const MAX_CHARACTERS = 2_000_000

export function sameRecoveryScope(left: RecoveryScope, right: RecoveryScope): boolean {
  return left.projectPath === right.projectPath && left.bookId === right.bookId && left.draftId === right.draftId
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function validResearch(value: unknown): value is ResearchRecoveryValue {
  return isRecord(value) && ['id', 'title', 'type', 'content'].every((key) => typeof value[key] === 'string') &&
    Array.isArray(value.tags) && value.tags.every((tag) => typeof tag === 'string') &&
    Array.isArray(value.entityRefs) && value.entityRefs.every((id) => typeof id === 'string')
}

function validEntry(value: unknown): value is RecoveryEntry {
  if (!isRecord(value) || typeof value.source !== 'string' || !isRecord(value.scope)) return false
  const scope = value.scope
  if (!['projectPath', 'bookId', 'draftId'].every((key) => typeof scope[key] === 'string')) return false
  return value.kind === 'scene'
    ? ['chapterGuid', 'sceneId', 'html', 'plainText', 'hash'].every((key) => typeof value[key] === 'string')
    : value.kind === 'research' && validResearch(value.draft) && validResearch(value.base)
}

export function readRecoveryJournal(): RecoveryEntry[] {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return []
  const entries: unknown = JSON.parse(raw)
  if (!Array.isArray(entries) || !entries.every(validEntry)) throw new Error('The mobile recovery journal is invalid.')
  return entries
}

function write(entries: RecoveryEntry[]): void {
  const raw = JSON.stringify(entries)
  if (raw.length > MAX_CHARACTERS) throw new Error('The mobile recovery journal is full. Save the current project before leaving the app.')
  if (entries.length) localStorage.setItem(STORAGE_KEY, raw)
  else localStorage.removeItem(STORAGE_KEY)
}

export function recordRecovery(entry: RecoveryEntry): void {
  const entries = readRecoveryJournal().filter((existing) =>
    !sameRecoveryScope(existing.scope, entry.scope) || existing.kind !== entry.kind || existing.source !== entry.source)
  write([...entries, entry])
}

export function removeRecovery(entry: RecoveryEntry): void {
  const serialized = JSON.stringify(entry)
  write(readRecoveryJournal().filter((current) => JSON.stringify(current) !== serialized))
}

export function researchValue(item: ResearchRecoveryValue): ResearchRecoveryValue {
  return { id: item.id, title: item.title, type: item.type, content: item.content, tags: item.tags, entityRefs: item.entityRefs }
}

export function sameResearchValue(left: ResearchRecoveryValue, right: ResearchRecoveryValue): boolean {
  return JSON.stringify(researchValue(left)) === JSON.stringify(researchValue(right))
}

export function remapRecoveryEntry(entry: RecoveryEntry, scope: RecoveryScope): RecoveryEntry | null {
  if (entry.scope.bookId !== scope.bookId || entry.scope.draftId !== scope.draftId) return null
  const entries = readRecoveryJournal()
  const index = entries.findIndex((current) => JSON.stringify(current) === JSON.stringify(entry))
  if (index < 0 || entries.some((current, at) => at !== index && sameRecoveryScope(current.scope, scope) && current.kind === entry.kind && current.source === entry.source)) return null
  const updated = { ...entry, scope }
  entries[index] = updated
  write(entries)
  return updated
}
