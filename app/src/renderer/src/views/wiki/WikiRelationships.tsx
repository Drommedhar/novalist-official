import { useTranslation } from 'react-i18next'
import { useWikiStore, type WikiRelationship } from '../../stores/wikiStore'

/** Role -> target links. Targets that did not resolve to a single entity render
 * as plain text (they never navigate). */
export function WikiRelationships({
  relationships,
  id
}: {
  relationships: WikiRelationship[]
  id?: string
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const openArticle = useWikiStore((s) => s.openArticle)

  if (relationships.length === 0) return null

  return (
    <section className="wiki-section" id={id}>
      <h2>{t('wiki.relationships')}</h2>
      <ul className="wiki-relationships">
        {relationships.map((rel, i) => (
          <li key={`${rel.role}-${i}`}>
            <span className="wiki-rel-role">{rel.role}</span>
            <span className="wiki-rel-targets">
              {rel.targets.map((target, j) => (
                <span key={`${target.name}-${j}`}>
                  {j > 0 && ', '}
                  {target.entityId && target.typeKey ? (
                    <button
                      type="button"
                      className="wiki-link"
                      onClick={() => void openArticle(target.typeKey!, target.entityId!)}
                    >
                      {target.name}
                    </button>
                  ) : (
                    <span className="wiki-rel-plain">{target.name}</span>
                  )}
                </span>
              ))}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
