import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface SceneTemplateDto {
  id: string
  name: string
  synopsis: string
  pov: string | null
  stage: string | null
  labelKey: string | null
  tags: string[]
  plotlineCount: number
  contentLength: number
}

/**
 * The scenes this book starts new scenes from.
 *
 * Authoring is deliberately not here: a template is made by pointing at a
 * scene that already reads the way you want, which is easier than describing
 * one in a form and is the only way the prose skeleton comes along. This is
 * where they are reviewed and removed.
 */
export function SceneTemplatesCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [templates, setTemplates] = useState<SceneTemplateDto[]>([])

  useEffect(() => {
    void rpc
      .request<SceneTemplateDto[]>('sceneTemplates/list')
      .then(setTemplates)
      .catch(() => setTemplates([]))
  }, [])

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('sceneTemplates.intro')}</div>

      {templates.length === 0 && <p className="codex-empty">{t('sceneTemplates.empty')}</p>}

      {templates.map((template) => (
        <div key={template.id} className="match-row">
          <span className="tag-name">{template.name}</span>
          <span className="settings-hint">
            {t('sceneTemplates.summary', {
              tags: template.tags.length,
              plotlines: template.plotlineCount
            })}
          </span>
          <button
            className="match-remove"
            title={t('sceneTemplates.delete')}
            aria-label={`${t('sceneTemplates.delete')}: ${template.name}`}
            onClick={() =>
              void rpc
                .request<SceneTemplateDto[]>('sceneTemplates/delete', [template.id])
                .then(setTemplates)
            }
          >
            <Trash2 size={13} strokeWidth={2} />
          </button>
        </div>
      ))}
    </div>
  )
}
