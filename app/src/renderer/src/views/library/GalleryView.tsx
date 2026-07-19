import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LayoutGrid, List } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import './library.css'

interface GalleryImage {
  path: string
  url: string
}

const baseName = (p: string): string => p.split('/').pop() ?? p
const stem = (p: string): string => {
  const name = baseName(p)
  const dot = name.lastIndexOf('.')
  return dot > 0 ? name.slice(0, dot) : name
}
const src = (url: string): string => `novalist-project://nl/${encodeURI(url)}`

export function GalleryView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [images, setImages] = useState<GalleryImage[]>([])
  const [search, setSearch] = useState('')
  const [listView, setListView] = useState(false)
  const [lightbox, setLightbox] = useState<GalleryImage | null>(null)
  const [menu, setMenu] = useState<{ x: number; y: number; img: GalleryImage } | null>(null)

  useEffect(() => {
    if (mainView !== 'gallery') return
    void rpc.request<GalleryImage[]>('gallery/list').then(setImages)
  }, [mainView])

  const query = search.trim().toLowerCase()
  const filtered =
    query.length === 0 ? images : images.filter((img) => img.path.toLowerCase().includes(query))

  const openMenu = (e: React.MouseEvent, img: GalleryImage): void => {
    e.preventDefault()
    setMenu({ x: e.clientX, y: e.clientY, img })
  }

  const menuActions = menu && [
    { label: t('imageGallery.copyPath'), run: () => window.novalist.copyText(menu.img.path) },
    {
      label: t('imageGallery.copyMarkdown'),
      run: () => window.novalist.copyText(`![${stem(menu.img.path)}](${menu.img.path})`)
    },
    { label: t('imageGallery.openExternally'), run: () => void window.novalist.openExternal(menu.img.url) },
    { label: t('imageGallery.openInExplorer'), run: () => void window.novalist.revealPath(menu.img.url) }
  ]

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
        <div className="gallery-viewtoggle">
          <button
            className={`gallery-viewtoggle-btn${listView ? '' : ' active'}`}
            title={t('imageGallery.gridView')}
            aria-label={t('imageGallery.gridView')}
            onClick={() => setListView(false)}
          >
            <LayoutGrid size={16} strokeWidth={2} />
          </button>
          <button
            className={`gallery-viewtoggle-btn${listView ? ' active' : ''}`}
            title={t('imageGallery.listView')}
            aria-label={t('imageGallery.listView')}
            onClick={() => setListView(true)}
          >
            <List size={16} strokeWidth={2} />
          </button>
        </div>
        <span className="inspector-meta">
          {t('imageGallery.countOf', {
            shown: filtered.length,
            total: images.length,
            defaultValue: '{{shown}} of {{total}}'
          })}
        </span>
      </div>
      {filtered.length === 0 ? (
        <p className="codex-empty">
          {query.length === 0 ? t('imageGallery.noImages') : t('imageGallery.noResults')}
        </p>
      ) : listView ? (
        <div className="gallery-list">
          {filtered.map((img) => (
            <button
              key={img.path}
              className="gallery-list-row"
              onClick={() => setLightbox(img)}
              onContextMenu={(e) => openMenu(e, img)}
            >
              <img className="gallery-list-thumb" src={src(img.url)} alt={img.path} loading="lazy" />
              <span className="gallery-list-text">
                <span className="gallery-list-name">{stem(img.path)}</span>
                <span className="gallery-list-path">{img.path}</span>
              </span>
            </button>
          ))}
        </div>
      ) : (
        <div className="gallery-grid">
          {filtered.map((img) => (
            <figure
              key={img.path}
              className="gallery-item"
              onClick={() => setLightbox(img)}
              onContextMenu={(e) => openMenu(e, img)}
            >
              <img src={src(img.url)} alt={img.path} loading="lazy" />
              <figcaption title={img.path}>{baseName(img.path)}</figcaption>
            </figure>
          ))}
        </div>
      )}
      {lightbox && (
        <div className="gallery-lightbox" onClick={() => setLightbox(null)}>
          <img src={src(lightbox.url)} alt={lightbox.path} />
          <span className="gallery-lightbox-name">{baseName(lightbox.path)}</span>
        </div>
      )}
      {menu && menuActions && (
        <>
          <div
            className="gallery-menu-scrim"
            onClick={() => setMenu(null)}
            onContextMenu={(e) => {
              e.preventDefault()
              setMenu(null)
            }}
          />
          <div className="gallery-menu" style={{ left: menu.x, top: menu.y }}>
            {menuActions.map((action) => (
              <button
                key={action.label}
                onClick={() => {
                  action.run()
                  setMenu(null)
                }}
              >
                {action.label}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
