import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useShellStore } from '../stores/shellStore'
import { useWikiStore } from '../stores/wikiStore'
import { type CardPeek, type EntityCard } from './contextTypes'
import { MotionPresence } from './MotionPresence'

/** A single set of section-collapse preferences (not scene-specific), persisted
 * to localStorage. Mirrors the desktop ProjectSettings.ViewState.Context* flags,
 * but without adding a settings facade. */
export const SECTION_STORAGE_KEY = 'nl.context.collapsed'

export function readCollapsed(): Record<string, boolean> {
  try {
    return JSON.parse(localStorage.getItem(SECTION_STORAGE_KEY) || '{}') as Record<string, boolean>
  } catch {
    return {}
  }
}

/** Localizes a backend key ("tense", "sceneTag.dialogue", "pov.firstPerson"),
 * falling back to the raw value when it is a plain name. */
export function loc(t: (k: string) => string, value: string, prefix?: string): string {
  if (!value) return value
  if (value.includes('.')) {
    const translated = t(value)
    return translated === value ? value.split('.').pop()! : translated
  }
  if (prefix) {
    const translated = t(`${prefix}.${value}`)
    if (translated !== `${prefix}.${value}`) return translated
  }
  return value
}

export function CollapsibleSection({
  titleKey,
  sectionKey,
  collapsed,
  onToggle,
  children
}: {
  titleKey: string
  sectionKey: string
  collapsed: boolean
  onToggle(key: string): void
  children: React.ReactNode
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="ctx-section">
      <button
        className="ctx-section-head"
        aria-expanded={!collapsed}
        onClick={() => onToggle(sectionKey)}
      >
        {collapsed ? (
          <ChevronRight className="ctx-section-chevron" size={12} strokeWidth={2} />
        ) : (
          <ChevronDown className="ctx-section-chevron" size={12} strokeWidth={2} />
        )}
        <span className="ctx-section-title">{t(titleKey)}</span>
      </button>
      <MotionPresence collapse>
        {!collapsed && <div className="ctx-section-content">{children}</div>}
      </MotionPresence>
    </div>
  )
}

export function EntitySection({
  titleKey,
  sectionKey,
  type,
  cards,
  collapsed,
  onToggle,
  peek
}: {
  titleKey: string
  sectionKey: string
  type: string
  cards: EntityCard[]
  collapsed: boolean
  onToggle(key: string): void
  peek: CardPeek
}): React.JSX.Element | null {
  const { t } = useTranslation()
  if (cards.length === 0) return null
  const open = async (id: string): Promise<void> => {
    useShellStore.getState().setMainView('wiki')
    await useWikiStore.getState().openArticle(type, id)
  }
  return (
    <CollapsibleSection
      titleKey={titleKey}
      sectionKey={sectionKey}
      collapsed={collapsed}
      onToggle={onToggle}
    >
      {cards.map((card) => (
        <button
          key={card.id}
          className="ctx-card"
          onClick={() => void open(card.id)}
          onMouseEnter={(e) => peek.onEnter(type, card.id, e.currentTarget)}
          onMouseLeave={() => peek.onLeave()}
        >
          {card.imagePath ? (
            <img
              className="ctx-card-img"
              src={`novalist-project://nl/${encodeURI(card.imagePath)}`}
              alt=""
            />
          ) : (
            <span className="ctx-card-img ctx-card-img-empty" aria-hidden="true">
              {card.name.slice(0, 1).toUpperCase()}
            </span>
          )}
          <span className="ctx-card-text">
            <span className="ctx-card-name">{card.name}</span>
            {card.detail && <span className="ctx-card-detail">{card.detail}</span>}
            {card.secondary && <span className="ctx-card-detail">{card.secondary}</span>}
            {(card.gender || card.age) && (
              <span className="ctx-card-pills">
                {card.gender && (
                  <span className="entity-chip">
                    {t('context.genderPill')} {card.gender}
                  </span>
                )}
                {card.age && (
                  <span className="entity-chip">
                    {t('context.agePill')} {card.age}
                  </span>
                )}
              </span>
            )}
          </span>
        </button>
      ))}
    </CollapsibleSection>
  )
}
