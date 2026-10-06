import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderPlus, ImagePlus, LayoutGrid, List, Tag } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useContentTransition } from '../../shell/useContentTransition'
import { MotionPresence } from '../../shell/MotionPresence'
import './library.css'

interface GalleryImage {
  path: string
  url: string
  collection: string
  tags: string[]
}

/** The pictures plus the vocabulary already in use over them. */
interface GalleryCatalog {
  images: GalleryImage[]
  collections: string[]
  tags: string[]
}

const baseName = (p: string): string => p.split('/').pop() ?? p
const stem = (p: string): string => {
  const name = baseName(p)
  const dot = name.lastIndexOf('.')
  return dot > 0 ? name.slice(0, dot) : name
}
const src = (url: string): string => `novalist-project://nl/${encodeURI(url)}`

function useGallery() {
  const { t } = useTranslation()
  const [catalog, setCatalog] = useState<GalleryCatalog>({
    images: [],
    collections: [],
    tags: []
  })
  const images = catalog.images
  const [search, setSearch] = useState('')
  // Empty means every picture, which is what the Gallery always showed.
  const [collection, setCollection] = useState('')
  const [tag, setTag] = useState('')
  const [listView, setListView] = useState(false)
  const [lightbox, setLightbox] = useState<GalleryImage | null>(null)
  const lightboxRef = useRef<HTMLDialogElement>(null)
  const [menu, setMenu] = useState<{ x: number; y: number; img: GalleryImage } | null>(null)

  const load = (): void => {
    void rpc.request<GalleryCatalog>('gallery/catalog').then(setCatalog)
  }

  useEffect(() => {
    load()
  }, [])

  useEffect(() => {
    if (lightbox) lightboxRef.current?.showModal()
  }, [lightbox])

  const closeLightbox = (): void => {
    lightboxRef.current?.close()
    setLightbox(null)
  }

  const query = search.trim().toLowerCase()
  const filtered = images.filter(
    (img) =>
      (query.length === 0 || img.path.toLowerCase().includes(query)) &&
      (collection.length === 0 || img.collection === collection) &&
      (tag.length === 0 || img.tags.includes(tag))
  )

  /* Copied in rather than pointed at, the same as every other import: a path
     into somebody's Downloads folder is a file that will be gone by the time
     anyone follows it. */
  const importImages = (): void => {
    void window.novalist.pickFile(t('imageGallery.import'), 'images').then((path) => {
      if (!path) return
      void rpc.request('gallery/import', [path]).then(load)
    })
  }

  const fileInto = (img: GalleryImage): void => {
    const next = window.prompt(t('imageGallery.collectionPrompt'), img.collection)
    if (next === null) return
    void rpc.request<GalleryCatalog>('gallery/setCollection', [img.path, next]).then(setCatalog)
  }

  const retag = (img: GalleryImage): void => {
    const next = window.prompt(t('imageGallery.tagsPrompt'), img.tags.join(', '))
    if (next === null) return
    void rpc
      .request<GalleryCatalog>('gallery/setTags', [img.path, next.split(',')])
      .then(setCatalog)
  }

  const openMenu = (e: React.MouseEvent, img: GalleryImage): void => {
    e.preventDefault()
    setMenu({ x: e.clientX, y: e.clientY, img })
  }

  const menuActions = menu && galleryMenuActions(menu.img, t, fileInto, retag)

  return { t, search, setSearch, catalog, collection, setCollection, tag, setTag, importImages, listView, setListView, filtered, images, query, setLightbox, openMenu, lightbox, lightboxRef, closeLightbox, menu, menuActions, setMenu }
}

export function GalleryView(): React.JSX.Element {
  const { t, search, setSearch, catalog, collection, setCollection, tag, setTag, importImages, listView, setListView, filtered, images, query, setLightbox, openMenu, lightbox, lightboxRef, closeLightbox, menu, menuActions, setMenu } = useGallery()
  const galleryRef = useRef<HTMLDivElement>(null)
  useContentTransition(galleryRef, `${listView}:${collection}:${tag}`)

  return (
    <div className="gallery" ref={galleryRef}>
      <GalleryToolbar t={t} search={search} setSearch={setSearch} catalog={catalog} collection={collection} setCollection={setCollection} tag={tag} setTag={setTag} importImages={importImages} listView={listView} setListView={setListView} filtered={filtered} images={images} />
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
              <img
                className="gallery-list-thumb"
                src={src(img.url)}
                alt={img.path}
                loading="lazy"
              />
              <span className="gallery-list-text">
                <span className="gallery-list-name">{stem(img.path)}</span>
                <span className="gallery-list-path">{img.path}</span>
                {(img.collection || img.tags.length > 0) && (
                  <span className="gallery-filing">
                    {img.collection && (
                      <span className="gallery-chip">
                        <FolderPlus size={11} strokeWidth={2} /> {img.collection}
                      </span>
                    )}
                    {img.tags.map((name) => (
                      <span key={name} className="gallery-chip">
                        <Tag size={11} strokeWidth={2} /> {name}
                      </span>
                    ))}
                  </span>
                )}
              </span>
            </button>
          ))}
        </div>
      ) : (
        <div className="gallery-grid">
          {filtered.map((img) => (
            <button
              type="button"
              key={img.path}
              className="gallery-item"
              onClick={() => setLightbox(img)}
              onContextMenu={(e) => openMenu(e, img)}
            >
              <img src={src(img.url)} alt={img.path} loading="lazy" />
              <span className="gallery-caption" title={img.path}>
                <strong>{stem(img.path)}</strong>
                <small>{img.collection || img.path}</small>
              </span>
            </button>
          ))}
        </div>
      )}
      <MotionPresence>{lightbox && (
        <dialog
          ref={lightboxRef}
          className="gallery-lightbox"
          aria-label={baseName(lightbox.path)}
          onCancel={(event) => { event.preventDefault(); closeLightbox() }}
          onClick={(event) => {
            if (event.target === event.currentTarget) closeLightbox()
          }}
        >
          <img src={src(lightbox.url)} alt={lightbox.path} />
          <span className="gallery-lightbox-name">{baseName(lightbox.path)}</span>
          <button className="dialog-button" onClick={closeLightbox}>
            {t('dialog.close')}
          </button>
        </dialog>
      )}</MotionPresence>
      <MotionPresence>{menu && menuActions && (
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
      )}</MotionPresence>
    </div>
  )
}

function GalleryToolbar({ t, search, setSearch, catalog, collection, setCollection, tag, setTag, importImages, listView, setListView, filtered, images }: { t: GalleryViewState['t']; search: GalleryViewState['search']; setSearch: GalleryViewState['setSearch']; catalog: GalleryViewState['catalog']; collection: GalleryViewState['collection']; setCollection: GalleryViewState['setCollection']; tag: GalleryViewState['tag']; setTag: GalleryViewState['setTag']; importImages: GalleryViewState['importImages']; listView: GalleryViewState['listView']; setListView: GalleryViewState['setListView']; filtered: GalleryViewState['filtered']; images: GalleryViewState['images'] }): React.JSX.Element {
  return (
    <div className="timeline-toolbar">
      <input
        className="dialog-input relationships-search"
        placeholder={t('imageGallery.search')}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      {/* Only offered once something is filed. A picker with one entry
          reading "everything" is a control that cannot do anything. */}
      {catalog.collections.length > 0 && (
        <select
          className="dialog-input gallery-filter"
          aria-label={t('imageGallery.collection')}
          value={collection}
          onChange={(e) => setCollection(e.target.value)}
        >
          <option value="">{t('imageGallery.allCollections')}</option>
          {catalog.collections.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      )}
      {catalog.tags.length > 0 && (
        <select
          className="dialog-input gallery-filter"
          aria-label={t('imageGallery.tag')}
          value={tag}
          onChange={(e) => setTag(e.target.value)}
        >
          <option value="">{t('imageGallery.allTags')}</option>
          {catalog.tags.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      )}
      <DesktopViewActions>
        {' '}
        <button className="dialog-button primary" onClick={importImages}>
          <ImagePlus size={14} strokeWidth={2} /> {t('imageGallery.import')}
        </button>
      </DesktopViewActions>
      <div className="toolbar-spacer" />
      <div className="gallery-viewtoggle">
        <button
          className={`gallery-viewtoggle-btn${listView ? '' : ' active'}`}
          title={t('imageGallery.gridView')}
          aria-label={t('imageGallery.gridView')}
          onClick={() => setListView(false)}
        >
          <LayoutGrid size={16} strokeWidth={2} /> {t('imageGallery.gridView')}
        </button>
        <button
          className={`gallery-viewtoggle-btn${listView ? ' active' : ''}`}
          title={t('imageGallery.listView')}
          aria-label={t('imageGallery.listView')}
          onClick={() => setListView(true)}
        >
          <List size={16} strokeWidth={2} /> {t('imageGallery.listView')}
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
  )
}

type GalleryViewState = ReturnType<typeof useGallery>

function galleryMenuActions(image: GalleryImage, t: ReturnType<typeof useTranslation>['t'], fileInto: (image: GalleryImage) => void, retag: (image: GalleryImage) => void) {
  return [
    { label: t('imageGallery.copyPath'), run: () => window.novalist.copyText(image.path) },
    {
      label: t('imageGallery.copyMarkdown'),
      run: () => window.novalist.copyText(`![${stem(image.path)}](${image.path})`)
    },
    {
      label: t('imageGallery.openExternally'),
      run: () => void window.novalist.openExternal(image.url)
    },
    {
      label: t('imageGallery.openInExplorer'),
      run: () => void window.novalist.revealPath(image.url)
    },
    { label: t('imageGallery.fileInto'), run: () => fileInto(image) },
    { label: t('imageGallery.retag'), run: () => retag(image) }
  ]

}
