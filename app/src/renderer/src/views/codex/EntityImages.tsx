import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ImagePlus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'

interface EntityImage {
  name: string
  path: string
}

/** Image strip for the selected entity: gallery pick, import, remove. */
export function EntityImages(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const [galleryOpen, setGalleryOpen] = useState(false)
  const [galleryImages, setGalleryImages] = useState<string[]>([])

  if (!record || !selectedId) return null
  const images = Array.isArray(record.images) ? (record.images as EntityImage[]) : []

  const applyResult = (updated: Record<string, unknown>): void => {
    useCodexStore.setState({ selectedRecord: updated })
    void useCodexStore.getState().refresh()
  }

  const addFromGallery = async (): Promise<void> => {
    setGalleryImages(await rpc.request<string[]>('gallery/list'))
    setGalleryOpen(true)
  }

  const importImage = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('entityEditor.addImage'))
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

  return (
    <div className="entity-images">
      <div className="inspector-label">{t('entityEditor.images')}</div>
      <div className="entity-images-strip">
        {images.map((image) => (
          <figure key={image.path} className="entity-image">
            <img src={`novalist-project:///${encodeURI(image.path)}`} alt={image.name} />
            <button
              aria-label={`${t('explorer.contextDelete')} ${image.name}`}
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
          </figure>
        ))}
        <button className="entity-image-add" onClick={() => void addFromGallery()}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.fromGallery')}
        </button>
        <button className="entity-image-add" onClick={() => void importImage()}>
          <ImagePlus size={16} strokeWidth={1.75} />
          {t('entityEditor.importImage')}
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
              {galleryImages.map((path) => (
                <button
                  key={path}
                  className="entity-gallery-pick"
                  onClick={() => {
                    setGalleryOpen(false)
                    void rpc
                      .request<Record<string, unknown>>('entities/addImage', [
                        entityType,
                        selectedId,
                        path,
                        false
                      ])
                      .then(applyResult)
                  }}
                >
                  <img src={`novalist-project:///${encodeURI(path)}`} alt={path} loading="lazy" />
                </button>
              ))}
              {galleryImages.length === 0 && (
                <p className="codex-empty">{t('imageGallery.noImages')}</p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
