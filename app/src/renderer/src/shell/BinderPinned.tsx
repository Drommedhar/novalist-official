import { ChevronRight, Pin } from 'lucide-react'

import type { BinderState } from './binderState'

export function BinderPinned({ state }: { state: BinderState }): React.JSX.Element {
  const { t, binderTab, pinnedOpen, setPinnedOpen, openSceneId, openScene, pinned } = state
  return <>
        {/* Shown only when something is pinned. An empty "Pinned" heading is
            a permanent reminder of a feature, not a feature. */}
        {binderTab === 'chapters' && pinned.length > 0 && (
          <div className="binder-pinned">
            <button className="binder-pinned-head" onClick={() => setPinnedOpen((o) => !o)}>
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${pinnedOpen ? ' open' : ''}`}
              />
              {t('binder.pinned', { count: pinned.length })}
            </button>
            {pinnedOpen &&
              pinned.map(({ chapter, scene }) => (
                <div key={`pin-${scene.id}`} className="binder-scene-wrap">
                  <button
                    className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}`}
                    onClick={() => void openScene(chapter.guid, scene.id)}
                  >
                    <Pin size={11} strokeWidth={2} className="binder-pin-icon" />
                    <span className="binder-scene-title" title={scene.title}>{scene.title}</span>
                    {/* Which chapter it came from - a pinned list with no
                        context is a list of titles floating free of the book. */}
                    <span className="binder-pin-chapter">{chapter.title}</span>
                  </button>
                </div>
              ))}
          </div>
        )}
  </>
}
