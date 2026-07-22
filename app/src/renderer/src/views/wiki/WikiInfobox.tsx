import { useTranslation } from 'react-i18next'
import { useWikiStore, type WikiInfobox as Infobox } from '../../stores/wikiStore'

const IMAGE_PREFIX = 'novalist-project://nl/'

export function imageSrc(path: string): string {
  return IMAGE_PREFIX + encodeURI(path)
}

/** The right-hand fact panel: primary image, a label/value table, and a gallery
 * of any further images (each with its name as a caption). Built-in fields carry
 * an i18n key; custom fields a literal label. A field with a link target renders
 * as a cross-link to another article. Clicking any image opens it full-size. */
export function WikiInfobox({
  infobox,
  onImageClick
}: {
  infobox: Infobox
  onImageClick: (src: string) => void
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const openArticle = useWikiStore((s) => s.openArticle)

  const hasContent =
    infobox.primaryImageUrl != null || infobox.fields.length > 0 || infobox.images.length > 1
  if (!hasContent) return null

  // The primary image is shown on its own; the rest form the gallery.
  const gallery = infobox.images.slice(1)

  return (
    <aside className="wiki-infobox" aria-label={t('wiki.infobox')}>
      {infobox.primaryImageUrl && (
        <figure className="wiki-infobox-figure">
          <button
            type="button"
            className="wiki-image-btn"
            title={t('wiki.viewImage')}
            onClick={() => onImageClick(imageSrc(infobox.primaryImageUrl!))}
          >
            <img className="wiki-infobox-image" src={imageSrc(infobox.primaryImageUrl)} alt="" />
          </button>
          {infobox.images[0]?.name && (
            <figcaption className="wiki-infobox-caption">{infobox.images[0].name}</figcaption>
          )}
        </figure>
      )}

      {infobox.fields.length > 0 && (
        <dl className="wiki-infobox-fields">
          {infobox.fields.map((field, i) => (
            <div className="wiki-infobox-row" key={`${field.labelKey ?? field.literalLabel}-${i}`}>
              <dt>{field.labelKey ? t(field.labelKey) : field.literalLabel}</dt>
              <dd>
                {field.linkEntityId && field.linkTypeKey ? (
                  <button
                    type="button"
                    className="wiki-link"
                    onClick={() => void openArticle(field.linkTypeKey!, field.linkEntityId!)}
                  >
                    {field.value}
                  </button>
                ) : (
                  field.value
                )}
              </dd>
            </div>
          ))}
        </dl>
      )}

      {gallery.length > 0 && (
        <div className="wiki-gallery">
          {gallery.map((image, i) => (
            <figure className="wiki-gallery-item" key={`${image.url}-${i}`}>
              <button
                type="button"
                className="wiki-image-btn"
                title={t('wiki.viewImage')}
                onClick={() => onImageClick(imageSrc(image.url))}
              >
                <img src={imageSrc(image.url)} alt="" />
              </button>
              {image.name && <figcaption>{image.name}</figcaption>}
            </figure>
          ))}
        </div>
      )}
    </aside>
  )
}
