import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MapPin, Sparkles } from 'lucide-react'
import { FEATURE_DESIGN, NARRATOR, useNarrationStore, type NarrationBook, type VoiceScope } from '../../stores/narrationStore'
import { VoicePicker } from './NarrationVoicePicker'

/**
 * Voices for one stretch of the book.
 *
 * A character is not one voice for four hundred pages. They age, they are
 * injured, they are disguised, they are possessed, they are remembered as a
 * child in a chapter set thirty years earlier — and until this existed the only
 * way to say so was to edit the cast file by hand, or to design a second voice,
 * which silently destroyed the first because both were stored under one id.
 *
 * On the cast rail beside the standing voice, because it is the same statement
 * narrowed: who reads this person, here.
 */
export function VoiceScopeButton({
  characterId,
  name
}: {
  characterId: string
  name: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const scopes = useNarrationStore((s) => s.scopes)
  const book = useNarrationStore((s) => s.book)
  const voices = useNarrationStore((s) => s.voices)
  const designed = useNarrationStore((s) => s.designed)
  const engines = useNarrationStore((s) => s.engines)
  const busy = useNarrationStore((s) => s.busy)
  const setVoiceScope = useNarrationStore((s) => s.setVoiceScope)
  const openBrief = useNarrationStore((s) => s.openBrief)
  const [open, setOpen] = useState(false)
  const [where, setWhere] = useState('')

  const designer = engines.find((e) => e.isReady && (e.features & FEATURE_DESIGN) !== 0) ?? null
  const mine = scopes.filter((scope) => scope.characterId === characterId)
  const places = useMemo(() => placesIn(book), [book])
  const picked = places.find((place) => place.id === where) ?? null
  const who = characterId === NARRATOR ? null : characterId

  return (
    <>
      <button
        type="button"
        className={`narration-scope${mine.length > 0 ? ' set' : ''}`}
        title={t('narration.scopeFor', { name })}
        aria-label={t('narration.scopeFor', { name })}
        onClick={() => setOpen((was) => !was)}
      >
        <MapPin size={13} aria-hidden="true" />
      </button>
      {open && (
        <div className="narration-scope-editor">
          <p className="narration-scope-blurb">{t('narration.scopeBlurb')}</p>

          {mine.map((scope) => (
            <div key={scopeKey(scope)} className="narration-scope-row">
              <span className="narration-scope-where">{labelFor(scope, places, t)}</span>
              {/* Uncasting the stretch clears it, which sends those lines back
                  to the standing voice rather than silencing them. */}
              <VoicePicker
                voices={voices}
                designed={designed}
                value={scope.voiceId}
                label={t('narration.scopeFor', { name })}
                onChange={(id) =>
                  void setVoiceScope(
                    who,
                    { act: scope.act, chapter: scope.chapter, scene: scope.scene },
                    id
                  )
                }
              />
            </div>
          ))}

          {places.length === 0 ? (
            <p className="narration-chip warn">{t('narration.scopeNowhere')}</p>
          ) : (
            <div className="narration-scope-row">
              <select
                className="narration-voice"
                aria-label={t('narration.scopeWhere')}
                value={where}
                onChange={(e) => setWhere(e.target.value)}
              >
                <option value="">{t('narration.scopeWhere')}</option>
                {places.map((place) => (
                  <option key={place.id} value={place.id}>
                    {place.label}
                  </option>
                ))}
              </select>
              <VoicePicker
                voices={voices}
                designed={designed}
                value={null}
                label={t('narration.scopeVoice')}
                onChange={(id) => {
                  if (picked !== null) void setVoiceScope(who, picked.at, id)
                }}
              />
              {/* A voice designed for exactly this stretch. This is the half
                  that used to be impossible: designing a second voice for
                  somebody reused the first one's id and overwrote it, so asking
                  for an older Mira in Act Three destroyed how she sounded in
                  Act One. */}
              {designer !== null && who !== null && (
                <button
                  type="button"
                  className="narration-design"
                  disabled={busy || picked === null}
                  onClick={() => {
                    if (picked === null) return
                    setOpen(false)
                    void openBrief(who, false, picked.at)
                  }}
                >
                  <Sparkles size={12} strokeWidth={1.75} aria-hidden="true" />
                  {t('narration.scopeDesign')}
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </>
  )
}

/** One addressable stretch of the book. */
interface Place {
  id: string
  label: string
  at: { act: string | null; chapter: string | null; scene: string | null }
}

/**
 * Every stretch a voice can be set over, in reading order: each act, each
 * chapter, each scene.
 *
 * One flat list rather than three linked dropdowns. The writer is picking a
 * place they can already see in the book, and three dependent selects is a form
 * to fill in for what is one choice.
 */
function placesIn(book: NarrationBook | null): Place[] {
  if (book === null) return []
  const places: Place[] = []
  const acts = new Set<string>()

  for (const chapter of book.chapters) {
    if (chapter.act.trim().length > 0 && !acts.has(chapter.act)) {
      acts.add(chapter.act)
      places.push({
        id: `act:${chapter.act}`,
        label: chapter.act,
        at: { act: chapter.act, chapter: null, scene: null }
      })
    }
    // Addressed by guid, so renaming a chapter does not silently drop the voice
    // the writer set over it.
    places.push({
      id: `chapter:${chapter.guid}`,
      label: chapter.title,
      at: { act: null, chapter: chapter.guid, scene: null }
    })
    for (const scene of chapter.scenes) {
      places.push({
        id: `scene:${chapter.guid}:${scene.sceneTitle}`,
        label: `${chapter.title} — ${scene.sceneTitle}`,
        at: { act: null, chapter: chapter.guid, scene: scene.sceneTitle }
      })
    }
  }
  return places
}

/** A stretch by its identity rather than by its position, so a row removed
 *  takes its own contents with it. */
function scopeKey(scope: VoiceScope): string {
  return `${scope.act ?? ''}|${scope.chapter ?? ''}|${scope.scene ?? ''}`
}

/**
 * A stretch in the writer's own words.
 *
 * Matched back to the book where it can be, because a scope stores a chapter's
 * guid and a guid means nothing to a person. One written by hand, or pointing
 * at a chapter that has since been deleted, still has to say something — so it
 * falls back to what it actually holds rather than showing an empty row.
 */
function labelFor(
  scope: VoiceScope,
  places: Place[],
  t: (key: string, options?: Record<string, unknown>) => string
): string {
  const found = places.find(
    (place) =>
      (place.at.act ?? '') === (scope.act ?? '') &&
      (place.at.chapter ?? '') === (scope.chapter ?? '') &&
      (place.at.scene ?? '') === (scope.scene ?? '')
  )
  if (found) return found.label

  const said = [scope.act, scope.chapter, scope.scene].filter(
    (part) => part !== null && part.trim().length > 0
  )
  return said.length > 0 ? said.join(' — ') : t('narration.scopeElsewhere')
}
