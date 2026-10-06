import type { RefObject } from 'react'
import { rpc } from '../../rpc/client'
import { editorPane, useProjectStore } from '../../stores/projectStore'
import type { EditorWindow } from './editorBridge'

export interface EntityHit {
  id: string
  name: string
  detail: string
  imagePath: string | null
  type: string
}

interface EntityMatch {
  caseSensitive: boolean
  matchPlurals: boolean
  exclusions: string[]
  ignoredSceneIds: string[]
  plurals: string[]
}

interface EntityListRow extends Omit<EntityHit, 'type'> {
  aliases: string[]
  firstName: string | null
  surname: string | null
  match: EntityMatch | null
}

interface MatchedEntity {
  hit: EntityHit
  primaryName: string
  isAlias: boolean
  text: string
  match: EntityMatch | null
}

interface NameMatch {
  name: string
  entityId: string
  entityType: string
  isAlias: boolean
  caseSensitive?: boolean
  exclusions?: string[]
}

function collectMentionNames(byText: Map<string, MatchedEntity[]>, index: Map<string, EntityHit>) {
  const names: NameMatch[] = []
  const candidates: {
    entityId: string; entityType: string; primaryName: string
    matchedText: string; isAlias: boolean; subtitle: string
  }[] = []
  for (const [key, list] of byText) {
    if (list.length !== 1) continue
    const { hit, primaryName, isAlias, text, match } = list[0]
    index.set(key, hit)
    names.push({
      name: text, entityId: hit.id, entityType: hit.type, isAlias,
      caseSensitive: match?.caseSensitive, exclusions: match?.exclusions
    })
    candidates.push({
      entityId: hit.id, entityType: hit.type, primaryName, matchedText: text,
      isAlias, subtitle: hit.detail ?? ''
    })
  }
  return { names, candidates }
}

async function loadConlangNames(
  names: NameMatch[], byText: Map<string, MatchedEntity[]>
): Promise<Map<string, string> | null> {
  try {
    type Language = { id: string; name: string; words: { id: string; word: string }[] }
    const languages = await rpc.request<Language[]>('conlang/list')
    const seen = new Set<string>()
    const words = new Map<string, string>()
    for (const language of languages ?? []) {
      for (const word of language.words ?? []) {
        const text = (word.word ?? '').trim()
        // Short invented words are indistinguishable from common prose words.
        if (text.length < 3) continue
        const key = text.toLowerCase()
        if (seen.has(key) || byText.has(key)) continue
        seen.add(key)
        words.set(word.id, text)
        names.push({ name: text, entityId: word.id, entityType: 'conlang', isAlias: false })
      }
    }
    return words
  } catch {
    // Language metadata is optional; entity matching still works without it.
    return null
  }
}

export async function pushEditorEntityNames(
  editor: EditorWindow,
  pane: string,
  entityIndex: RefObject<Map<string, EntityHit>>,
  conlangWords: RefObject<Map<string, string>>
): Promise<void> {
  const index = new Map<string, EntityHit>()
  const byText = new Map<string, MatchedEntity[]>()
  const addText = (
    text: string, hit: EntityHit, primaryName: string, isAlias: boolean, match: EntityMatch | null
  ): void => {
    const trimmed = text.trim()
    const key = trimmed.toLowerCase()
    if (!key) return
    const list = byText.get(key) ?? []
    list.push({ hit, primaryName, isAlias, text: trimmed, match })
    byText.set(key, list)
  }
  const sceneId = editorPane(useProjectStore.getState(), pane).sceneId
  for (const type of ['character', 'location', 'item', 'lore']) {
    const list = await rpc.request<EntityListRow[]>('entities/list', [type])
    for (const entity of list) {
      const match = entity.match ?? null
      if (sceneId && match?.ignoredSceneIds.includes(sceneId)) continue
      const hit: EntityHit = { ...entity, type }
      index.set(entity.id, hit)
      addText(entity.name, hit, entity.name, false, match)
      if (entity.firstName) addText(entity.firstName, hit, entity.name, true, match)
      if (entity.surname) addText(entity.surname, hit, entity.name, true, match)
      for (const alias of entity.aliases ?? []) addText(alias, hit, entity.name, true, match)
      for (const plural of match?.plurals ?? []) addText(plural, hit, entity.name, true, match)
    }
  }
  const { names, candidates } = collectMentionNames(byText, index)
  const words = await loadConlangNames(names, byText)
  if (words) conlangWords.current = words
  entityIndex.current = index
  editor.setEntityNames(JSON.stringify(names))
  editor.setMentionCandidates(JSON.stringify(candidates))
}
