import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { type WikiAppearance } from '../../stores/wikiStore'

/** The Appearances timeline: every scene that mentions the entity, in story
 * order, showing its resolved story date and synopsis. Clicking opens the
 * scene in the editor. Doubles as the character/place timeline. */
export function WikiAppearances({
  appearances,
  id,
  bookName,
  multipleBooks
}: {
  appearances: WikiAppearance[]
  id?: string
  /** The book these appearances were gathered from. */
  bookName: string
  /** True when the project has more than one book, in which case the heading
   *  says which one this list covers — appearances are per-book. */
  multipleBooks: boolean
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const openScene = useProjectStore((s) => s.openScene)
  const setMainView = useShellStore((s) => s.setMainView)

  if (appearances.length === 0) return null

  const goToScene = async (chapterGuid: string, sceneId: string): Promise<void> => {
    await openScene(chapterGuid, sceneId)
    setMainView('write')
  }

  return (
    <section className="wiki-section" id={id}>
      <h2>
        {multipleBooks && bookName
          ? t('wiki.appearancesInBook', { book: bookName })
          : t('wiki.appearances')}
      </h2>
      <ol className="wiki-appearances">
        {appearances.map((app) => (
          <li key={`${app.chapterGuid}-${app.sceneId}`} className="wiki-appearance">
            <button
              type="button"
              className="wiki-appearance-btn"
              onClick={() => void goToScene(app.chapterGuid, app.sceneId)}
            >
              {app.storyDate && <span className="wiki-appearance-date">{app.storyDate}</span>}
              <span className="wiki-appearance-body">
                <span className="wiki-appearance-scene">
                  {app.chapterTitle} &middot; {app.sceneTitle}
                </span>
                {app.synopsis && <span className="wiki-appearance-synopsis">{app.synopsis}</span>}
              </span>
            </button>
          </li>
        ))}
      </ol>
    </section>
  )
}
