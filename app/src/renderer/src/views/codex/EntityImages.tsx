import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ClipboardPaste, ImagePlus, Link, Replace, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import './entity-images.css'

interface EntityImage {
  name: string
  /** What the picture shows, for a reader who cannot see it. */
  alt: string
  path: string
  /** Project-root-relative display URL resolved by the backend. */
  url: string
}

/** Image strip for the selected entity: gallery pick, import, clipboard paste,
 * download-from-URL, plus per-image rename and swap. */
export function EntityImages(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const [galleryOpen, setGalleryOpen] = useState(false)
  const [galleryImages, setGalleryImages] = useState<{ path: string; url: string }[]>([])
  // When set, the gallery picker swaps this stored path instead of adding.
  const [swapTarget, setSwapTarget] = useState<string | null>(null)
  const [urlOpen, setUrlOpen] = useState(false)
  const [urlValue, setUrlValue] = useState('')
  const [urlError, setUrlError] = useState(false)
  const [urlBusy, setUrlBusy] = useState(false)

  if (!record || !selectedId) return null
  const images = Array.isArray(record.images) ? (record.images as EntityImage[]) : []

  const applyResult = (updated: Record<string, unknown>): void => {
    useCodexStore.setState({ selectedRecord: updated })
    void useCodexStore.getState().refresh()
  }

  const openGallery = async (target: string | null): Promise<void> => {
    setSwapTarget(target)
    setGalleryImages(await rpc.request<{ path: string; url: string }[]>('gallery/list'))
    setGalleryOpen(true)
  }

  const pickFromGallery = (imagePath: string): void => {
    setGalleryOpen(false)
    const target = swapTarget
    setSwapTarget(null)
    const request = target
      ? rpc.request<Record<string, unknown>>('entities/replaceImage', [
          entityType,
          selectedId,
          target,
          imagePath
        ])
      : rpc.request<Record<string, unknown>>('entities/addImage', [
          entityType,
          selectedId,
          imagePath,
          false
        ])
    void request.then(applyResult)
  }

  const importImage = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('entityEditor.addImage'), 'images')
    if (!path) return
    applyResult(
      await rpc.request<Record<string, unknown>>('entities/addImage', [
        entityType,
        selectedId,
        path,
        true
      ])
    )
  }

  const pasteImage = async (): Promise<void> => {
    const path = await window.novalist.readClipboardImage()
    if (!path) return
    applyResult(
      await rpc.request<Record<string, unknown>>('entities/addImage', [
        entityType,
        selectedId,
        path,
        true
      ])
    )
  }

  const renameImage = (path: string, currentName: string, nextName: string): void => {
    if (nextName === currentName) return
    void rpc
      .request<Record<string, unknown>>('entities/renameImage', [
        entityType,
        selectedId,
        path,
        nextName
      ])
      .then(applyResult)
  }

  const submitUrl = async (): Promise<void> => {
    const url = urlValue.trim()
    if (!url) return
    setUrlBusy(true)
    setUrlError(false)
    try {
      const updated = await rpc.request<Record<string, unknown>>('entities/addImageFromUrl', [
        entityType,
        selectedId,
        url
      ])
      applyResult(updated)
      setUrlOpen(false)
      setUrlValue('')
    } catch {
      setUrlError(true)
    } finally {
      setUrlBusy(false)
    }
  }

  return (
    <div className="entity-images">
      <div className="inspector-label">{t('entityEditor.images')}</div>
      <div className="entity-images-strip">
        {images.map((image) => (
          <figure key={image.path} className="entity-image">
            <img
              src={`novalist-project://nl/${encodeURI(image.url)}`}
              alt={image.alt || image.name}
            />
            <div className="entity-image-actions">
              <button
                aria-label={`${t('entityEditor.chooseImageTooltip')} ${image.name}`}
                title={t('entityEditor.chooseImageTooltip')}
                onClick={() => void openGallery(image.path)}
              >
                <Replace size={11} strokeWidth={2} />
              </button>
              <button
                className="entity-image-remove"
                aria-label={`${t('entityEditor.removeImageTooltip')} ${image.name}`}
                title={t('entityEditor.removeImageTooltip')}
                onClick={() =>
                  void rpc
                    .request<Record<string, unknown>>('entities/removeImage', [
                      entityType,
                      selectedId,
                      image.path
                    ])
                    .then(applyResult)
                }
              >
                <X size={11} strokeWidth={2} />
              </button>
            </div>
            <input
              className="entity-image-namefield"
              aria-label={t('entityEditor.imageName')}
              placeholder={t('entityEditor.imageName')}
              defaultValue={image.name}
              key={`${image.path}:${image.name}`}
              onBlur={(e) => renameImage(image.path, image.name, e.target.value)}
            />
            {/* The name says which image this is; the description says what it
                shows. Only the second is any use read aloud, and only the
                second reaches an export. */}
            <input
              className="entity-image-namefield"
              aria-label={t('entityEditor.imageAlt')}
              placeholder={t('entityEditor.imageAltPlaceholder')}
              defaultValue={image.alt}
              key={`alt:${image.path}:${image.alt}`}
              onBlur={(e) => {
                if (e.target.value === image.alt) return
                void rpc
                  .request<Record<string, unknown>>('entities/setImageAlt', [
                    entityType,
                    selectedId,
                    image.path,
                    e.target.value
                  ])
                  .then(applyResult)
              }}
            />
          </figure>
        ))}
        <button className="entity-image-add" onClick={() => void openGallery(null)}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.fromGallery')}
        </button>
        <button className="entity-image-add" onClick={() => void importImage()}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.importImage')}
        </button>
        <button className="entity-image-add" onClick={() => void pasteImage()}>
          <ClipboardPaste size={16} strokeWidth={1.75} />
          {t('entityEditor.pasteImage')}
        </button>
        <button
          className="entity-image-add"
          onClick={() => {
            setUrlValue('')
            setUrlError(false)
            setUrlOpen(true)
          }}
        >
          <Link size={16} strokeWidth={1.75} />
          {t('entityEditor.fromUrl')}
        </button>
      </div>
      {galleryOpen && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => {
            if (e.target === e.currentTarget) {
              setGalleryOpen(false)
              setSwapTarget(null)
            }
          }}
        >
          <div className="dialog-card entity-gallery-card" role="dialog">
            <div className="dialog-title">
              {swapTarget ? t('entityEditor.chooseImageTooltip') : t('entityEditor.fromGallery')}
            </div>
            <div className="gallery-grid entity-gallery-grid">
              {galleryImages.map((img) => (
                <button
                  key={img.path}
                  className="entity-gallery-pick"
                  onClick={() => pickFromGallery(img.path)}
                >
                  <img
                    src={`novalist-project://nl/${encodeURI(img.url)}`}
                    alt={img.path}
                    loading="lazy"
                  />
                </button>
              ))}
              {galleryImages.length === 0 && (
                <p className="codex-empty">{t('imageGallery.noImages')}</p>
              )}
            </div>
          </div>
        </div>
      )}
      {urlOpen && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setUrlOpen(false)}
        >
          <div className="dialog-card" role="dialog">
            <div className="dialog-title">{t('entityEditor.fromUrlTitle')}</div>
            <input
              className="dialog-input entity-url-input"
              type="url"
              autoFocus
              placeholder={t('entityEditor.fromUrlPlaceholder')}
              value={urlValue}
              onChange={(e) => setUrlValue(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void submitUrl()}
            />
            {urlError && <div className="entity-url-error">{t('entityEditor.fromUrlError')}</div>}
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setUrlOpen(false)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button"
                disabled={urlBusy || urlValue.trim().length === 0}
                onClick={() => void submitUrl()}
              >
                {t('dialog.ok')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
