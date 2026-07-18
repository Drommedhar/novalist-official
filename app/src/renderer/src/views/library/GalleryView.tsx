import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'

export function GalleryView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [images, setImages] = useState<string[]>([])
  const [search, setSearch] = useState('')

  useEffect(() => {
    if (mainView !== 'gallery') return
    void rpc.request<string[]>('gallery/list').then(setImages)
  }, [mainView])

  const filtered = images.filter(
    (path) => search.length === 0 || path.toLowerCase().includes(search.toLowerCase())
  )

  return (
    <div className="gallery">
      <div className="timeline-toolbar">
        <input
          className="dialog-input relationships-search"
          placeholder={t('imageGallery.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <div className="toolbar-spacer" />
        <span className="inspector-meta">{filtered.length}</span>
      </div>
      {filtered.length === 0 ? (
        <p className="codex-empty">{t('imageGallery.noImages')}</p>
      ) : (
        <div className="gallery-grid">
          {filtered.map((path) => (
            <figure key={path} className="gallery-item">
              <img src={`novalist-project:///${encodeURI(path)}`} alt={path} loading="lazy" />
              <figcaption title={path}>{path.split('/').pop()}</figcaption>
            </figure>
          ))}
        </div>
      )}
    </div>
  )
}
