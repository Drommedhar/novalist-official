import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Bookmark as BookmarkIcon, Trash2 } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { useWikiStore } from '../stores/wikiStore'

export interface BookmarkDto {
  id: string
  kind: 'Scene' | 'Chapter' | 'Entity' | 'Research' | 'StoryDate' | 'MapPin'
  label: string
  group: string | null
  chapterGuid: string | null
  targetId: string | null
  targetType: string | null
  anchorText: string | null
  storyDate: string | null
  order: number
}

/**
 * Places worth coming back to.
 *
 * The favourite flag and saved lists answer "which scenes match this query".
 * A bookmark answers a different question — the paragraph where she finds out,
 * the entry I keep re-reading, the day the siege starts — and had nowhere to be
 * recorded, so people kept them in a scene called Notes.
 */
export function BookmarksPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [bookmarks, setBookmarks] = useState<BookmarkDto[]>([])
  const projectPath = useProjectStore((s) => s.projectPath)

  useEffect(() => {
    void rpc
      .request<BookmarkDto[]>('bookmarks/list')
      .then(setBookmarks)
      .catch(() => setBookmarks([]))
  }, [projectPath])

  const open = (bookmark: BookmarkDto): void => {
    switch (bookmark.kind) {
      case 'Scene':
        if (bookmark.chapterGuid && bookmark.targetId) {
          void useProjectStore.getState().openScene(bookmark.chapterGuid, bookmark.targetId)
        }
        break
      case 'Chapter':
        // A chapter opens at its first scene, which is what clicking one
        // anywhere else in the app does.
        if (bookmark.chapterGuid) {
          const chapter = useProjectStore
            .getState()
            .chapters.find((c) => c.guid === bookmark.chapterGuid)
          const first = chapter?.scenes[0]
          if (chapter && first) void useProjectStore.getState().openScene(chapter.guid, first.id)
        }
        break
      case 'Entity':
        if (bookmark.targetId) {
          useShellStore.getState().setMainView('wiki')
          void useWikiStore
            .getState()
            .openArticle(bookmark.targetType ?? 'character', bookmark.targetId)
        }
        break
      case 'Research':
        useShellStore.getState().setMainView('research')
        break
      case 'StoryDate':
        useShellStore.getState().setMainView('timeline')
        break
      case 'MapPin':
        useShellStore.getState().setMainView('maps')
        break
    }
  }

  const remove = (id: string): void => {
    void rpc.request<BookmarkDto[]>('bookmarks/delete', [id]).then(setBookmarks)
  }

  if (bookmarks.length === 0) {
    return <div className="binder-placeholder">{t('bookmarks.empty')}</div>
  }

  // The backend already returns them grouped-first, loose-last, so the order
  // is the one every surface showing bookmarks uses. This only keeps it.
  const groups = [...new Set(bookmarks.map((b) => b.group ?? ''))]

  return (
    <div className="bookmarks-panel">
      {groups.map((group) => (
        <div key={group || 'ungrouped'} className="bookmarks-group">
          <div className="binder-act">{group || t('bookmarks.ungrouped')}</div>
          {bookmarks
            .filter((b) => (b.group ?? '') === group)
            .map((bookmark) => (
              <div key={bookmark.id} className="bookmarks-row">
                <button className="binder-scene-row" onClick={() => open(bookmark)}>
                  <BookmarkIcon size={13} strokeWidth={2} />
                  <span className="binder-scene-title">{bookmark.label}</span>
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('bookmarks.remove')}
                  onClick={() => remove(bookmark.id)}
                >
                  <Trash2 size={13} strokeWidth={2} />
                </button>
              </div>
            ))}
        </div>
      ))}
    </div>
  )
}
