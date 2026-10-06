import { useTranslation } from 'react-i18next'
import { Sparkles, Trash2 } from 'lucide-react'
import { useNarrationStore } from '../../stores/narrationStore'

/** The narrator's own design button. Separate from a character's because the
 *  narrator has no Codex entry: their brief comes from the book. */
export function NarratorDesignActions({ voiceId }: { voiceId: string | null }): React.JSX.Element {
  const openNarratorBrief = useNarrationStore((s) => s.openNarratorBrief)
  return <VoiceDesignActions voiceId={voiceId} onDesign={() => void openNarratorBrief()} />
}

/** Design, re-design and forget, on the character's own row. */
export function DesignActions({
  characterId,
  voiceId
}: {
  characterId: string
  voiceId: string | null
}): React.JSX.Element {
  const openBrief = useNarrationStore((s) => s.openBrief)
  return <VoiceDesignActions voiceId={voiceId} onDesign={() => void openBrief(characterId)} />
}

function VoiceDesignActions({ voiceId, onDesign }: {
  voiceId: string | null
  onDesign: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const designed = useNarrationStore((s) => s.designed)
  const busy = useNarrationStore((s) => s.busy)
  const forgetVoice = useNarrationStore((s) => s.forgetVoice)

  const own = designed.find((d) => d.voiceId === voiceId) ?? null

  return (
    <span className="narration-design-actions">
      <button
        type="button"
        className="narration-design"
        disabled={busy}
        onClick={onDesign}
      >
        <Sparkles size={12} strokeWidth={1.75} aria-hidden="true" />
        {own === null ? t('narration.designVoice') : t('narration.redesignVoice')}
      </button>
      {own !== null && (
        <button
          type="button"
          className="narration-forget"
          aria-label={t('narration.forgetVoice')}
          title={t('narration.forgetVoice')}
          disabled={busy}
          onClick={() => void forgetVoice(own.voiceId)}
        >
          <Trash2 size={12} strokeWidth={1.75} aria-hidden="true" />
        </button>
      )}
    </span>
  )
}
