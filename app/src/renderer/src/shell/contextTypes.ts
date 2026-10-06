

/** Callbacks that let an entity card raise/dismiss the shared focus-peek overlay,
 * threaded from the panel down into each card. */
export interface CardPeek {
  onEnter(type: string, id: string, el: HTMLElement): void
  onLeave(): void
}

export interface EntityCard {
  id: string
  name: string
  detail: string
  secondary: string | null
  imagePath: string | null
  gender?: string | null
  age?: string | null
}

interface MentionCell {
  chapterLabel: string
  present: boolean
  current: boolean
}

interface MentionRow {
  name: string
  cells: MentionCell[]
  lastSeenChaptersAgo: number
}

/**
 * How this scene sits against what the book says it is written in.
 *
 * A reading of "unknown" means the prose was too short to be evidence or the
 * language does not mark tense with verb forms; nothing is flagged then.
 */
interface VoiceDrift {
  declaredPerson: string
  declaredTense: string
  personReading: string
  tenseReading: string
  personDrifts: boolean
  tenseDrifts: boolean
  /** 0-100. Below roughly 40 this is a question, not a verdict. */
  confidence: number
}

interface SceneAnalysis {
  pov: string
  povOptions: string[]
  emotion: string
  emotionKeys: string[]
  intensity: number
  conflict: string
  tags: string[]
  dialoguePercent: number
  avgSentenceLength: number
  wordCount: number
  /** False when the writing language is not English: emotion/intensity/conflict/
   *  tags are not auto-detected there, only whatever you set yourself. */
  keywordAnalysisSupported: boolean
  voiceDrift: VoiceDrift | null
}

export interface SceneContext {
  characters: EntityCard[]
  locations: EntityCard[]
  items: EntityCard[]
  lore: EntityCard[]
  mentionRows: MentionRow[]
  analysis: SceneAnalysis
  research: ResearchSuggestion[]
}

/** A research item this scene is about, and what matched. */
interface ResearchSuggestion {
  id: string
  title: string
  type: string
  reason: string
}
