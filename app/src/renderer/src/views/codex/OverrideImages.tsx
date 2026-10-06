import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ClipboardPaste, ImagePlus, Link, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { type OverrideImage, type CharacterOverride, type Scope, apply } from './overrideModel'
import { OverrideMediaHeader } from './OverrideMediaHeader'

/** Per-scope override image strip: gallery pick, file import, clipboard paste and
 * download-from-URL, plus per-image remove/rename and reset-to-inherit. Mirrors
 * EntityImages.tsx but persists to the override scope via entities/setOverride*. */
export function OverrideImages({
  selectedId,
  scope,
  override,
  baseImages
}: {
  selectedId: string
  scope: Scope
  override: CharacterOverride | undefined
  baseImages: OverrideImage[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const overriding = Array.isArray(override?.images)
  const images = overriding ? (override!.images as OverrideImage[]) : baseImages
  const [galleryOpen, setGalleryOpen] = useState(false)
  const [galleryImages, setGalleryImages] = useState<{ path: string; url: string }[]>([])
  const [urlOpen, setUrlOpen] = useState(false)
  const [urlValue, setUrlValue] = useState('')
  const [urlError, setUrlError] = useState(false)
  const [urlBusy, setUrlBusy] = useState(false)

  const args = [selectedId, scope.chapter, scope.scene] as const

  const setImages = (next: OverrideImage[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideImages', [
        ...args,
        next?.map((i) => ({ name: i.name, path: i.path })) ?? null
      ])
      .then(apply)
  }

  const openGallery = async (): Promise<void> => {
    setGalleryImages(await rpc.request<{ path: string; url: string }[]>('gallery/list'))
    setGalleryOpen(true)
  }

  const pickFromGallery = (imagePath: string): void => {
    setGalleryOpen(false)
    const name = imagePath.split('/').pop()?.replace(/\.[^.]+$/, '') ?? imagePath
    setImages([...images, { name, path: imagePath }])
  }

  const importImage = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('entityEditor.addImage'), 'images')
    if (!path) return
    apply(await rpc.request<Record<string, unknown>>('entities/addOverrideImage', [...args, path]))
  }

  const pasteImage = async (): Promise<void> => {
    const path = await window.novalist.readClipboardImage()
    if (!path) return
    apply(await rpc.request<Record<string, unknown>>('entities/addOverrideImage', [...args, path]))
  }

  const submitUrl = async (): Promise<void> => {
    const url = urlValue.trim()
    if (!url) return
    setUrlBusy(true)
    setUrlError(false)
    try {
      apply(
        await rpc.request<Record<string, unknown>>('entities/addOverrideImageFromUrl', [...args, url])
      )
      setUrlOpen(false)
      setUrlValue('')
    } catch {
      setUrlError(true)
    } finally {
      setUrlBusy(false)
    }
  }

  const renameImage = (path: string, currentName: string, nextName: string): void => {
    if (nextName === currentName) return
    setImages(images.map((i) => (i.path === path ? { ...i, name: nextName } : i)))
  }

  return (
    <div className="entity-images overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.images"
        overriding={overriding}
        onReset={() => setImages(null)}
      />
      <div className="entity-images-strip">
        {images.map((image) => (
          <figure key={image.path} className="entity-image">
            <img
              src={`novalist-project://nl/${encodeURI(image.url ?? image.path)}`}
              alt={image.name}
            />
            <div className="entity-image-actions">
              <button
                className="entity-image-remove"
                aria-label={`${t('entityEditor.removeImageTooltip')} ${image.name}`}
                title={t('entityEditor.removeImageTooltip')}
                onClick={() => setImages(images.filter((i) => i.path !== image.path))}
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
          </figure>
        ))}
        <button className="entity-image-add" onClick={() => void openGallery()}>
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
          onPointerDown={(e) => e.target === e.currentTarget && setGalleryOpen(false)}
        >
          <div className="dialog-card entity-gallery-card" role="dialog">
            <div className="dialog-title">{t('entityEditor.fromGallery')}</div>
            <div className="gallery-grid entity-gallery-grid">
              {galleryImages.map((img) => (
                <button
                  key={img.path}
                  className="entity-gallery-pick"
                  onClick={() => pickFromGallery(img.path)}
                >
                  <img src={`novalist-project://nl/${encodeURI(img.url)}`} alt={img.path} loading="lazy" />
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
