import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, ArrowRight, Plus, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

interface SceneLink {
  id: string
  kind: string
  targetId: string
  /** Empty when the thing at the other end is gone. */
  targetTitle: string
  note: string
}

interface Backlink {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  note: string
}

interface ResearchItem {
  id: string
  title: string
}

interface EntityOption {
  id: string
  name: string
}

const ENTITY_KINDS = ['character', 'location', 'item', 'lore']

/**
 * What a scene points at, and what points back at it.
 *
 * Research items could already reference each other both ways. A scene could
 * reference nothing: a scene that answers another scene, or leans on one
 * research note, could only say so as prose in its own notes — which nothing
 * could follow, and which the other end never knew about.
 */
export function LinksPanel(props: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openScene = useProjectStore((s) => s.openScene)
  const setMainView = useShellStore((s) => s.setMainView)
  const [links, setLinks] = useState<SceneLink[]>([])
  const [backlinks, setBacklinks] = useState<Backlink[]>([])
  const [kind, setKind] = useState('scene')
  const [target, setTarget] = useState('')
  const [research, setResearch] = useState<ResearchItem[]>([])
  const [entities, setEntities] = useState<EntityOption[]>([])

  const load = useCallback(() => {
    void rpc
      .request<SceneLink[]>('links/list', [props.chapterGuid, props.sceneId])
      .then(setLinks)
      .catch(() => setLinks([]))
    void rpc
      .request<Backlink[]>('links/backlinks', ['scene', props.sceneId])
      .then(setBacklinks)
      .catch(() => setBacklinks([]))
  }, [props.chapterGuid, props.sceneId])

  useEffect(load, [load])

  // Loaded once rather than per open: the lists a picker offers do not change
  // while somebody is deciding what to point at.
  useEffect(() => {
    void rpc
      .request<ResearchItem[]>('research/list')
      .then(setResearch)
      .catch(() => setResearch([]))
    void Promise.all(
      ENTITY_KINDS.map((k) =>
        rpc.request<EntityOption[]>('entities/list', [k]).catch(() => [] as EntityOption[])
      )
    ).then((lists) =>
      setEntities(
        lists
          .flat()
          .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
      )
    )
  }, [])

  const choices = (): { id: string; label: string }[] => {
    if (kind === 'research') return research.map((r) => ({ id: r.id, label: r.title }))
    if (kind === 'entity') return entities.map((e) => ({ id: e.id, label: e.name }))
    return chapters.flatMap((chapter) =>
      chapter.scenes
        // A scene pointing at itself is a link that answers nothing.
        .filter((scene) => scene.id !== props.sceneId)
        .map((scene) => ({ id: scene.id, label: `${chapter.title} - ${scene.title}` }))
    )
  }

  const add = (): void => {
    if (!target) return
    void rpc
      .request<SceneLink[]>('links/add', [props.chapterGuid, props.sceneId, kind, target])
      .then((next) => {
        setLinks(next)
        setTarget('')
      })
  }

  const open = (link: SceneLink): void => {
    if (link.kind === 'research') {
      setMainView('research')
      return
    }
    if (link.kind === 'entity') {
      setMainView('codex')
      return
    }
    const chapter = chapters.find((c) => c.scenes.some((s) => s.id === link.targetId))
    if (chapter) void openScene(chapter.guid, link.targetId)
  }

  return (
    <div className="links-panel">
      <div className="inspector-label">{t('links.pointsAt')}</div>
      {links.length === 0 && <p className="settings-hint">{t('links.none')}</p>}
      {links.map((link) => (
        <div key={link.id} className="links-row">
          <ArrowRight size={12} strokeWidth={2} className="links-arrow" />
          {/* A target that is gone keeps its row and says so. A link that
              disappears silently is one the writer never finds out they lost. */}
          <button
            className="links-target"
            disabled={link.targetTitle.length === 0}
            onClick={() => open(link)}
          >
            {link.targetTitle || t('links.missing')}
          </button>
          <input
            className="links-note"
            placeholder={t('links.notePlaceholder')}
            defaultValue={link.note}
            onBlur={(e) => {
              if (e.target.value === link.note) return
              void rpc
                .request<SceneLink[]>('links/setNote', [
                  props.chapterGuid,
                  props.sceneId,
                  link.id,
                  e.target.value
                ])
                .then(setLinks)
            }}
          />
          <button
            className="binder-row-action"
            aria-label={t('links.remove')}
            title={t('links.remove')}
            onClick={() =>
              void rpc
                .request<SceneLink[]>('links/remove', [
                  props.chapterGuid,
                  props.sceneId,
                  link.id
                ])
                .then(setLinks)
            }
          >
            <X size={12} strokeWidth={2} />
          </button>
        </div>
      ))}

      <div className="links-row">
        <select
          className="inspector-input links-kind"
          aria-label={t('links.kind')}
          value={kind}
          onChange={(e) => {
            setKind(e.target.value)
            setTarget('')
          }}
        >
          <option value="scene">{t('links.kindScene')}</option>
          <option value="research">{t('links.kindResearch')}</option>
          <option value="entity">{t('links.kindEntity')}</option>
        </select>
        <select
          className="inspector-input"
          aria-label={t('links.target')}
          value={target}
          onChange={(e) => setTarget(e.target.value)}
        >
          <option value="">{t('links.pick')}</option>
          {choices().map((choice) => (
            <option key={choice.id} value={choice.id}>
              {choice.label}
            </option>
          ))}
        </select>
        <button className="btn-secondary" disabled={!target} onClick={add}>
          <Plus size={12} strokeWidth={2} />
        </button>
      </div>

      {/* The half that makes a link worth making. Without it a scene has no way
          to know which scenes answer it. */}
      {backlinks.length > 0 && (
        <>
          <div className="inspector-label">{t('links.pointedAtBy')}</div>
          {backlinks.map((back) => (
            <div key={`${back.sceneId}-${back.note}`} className="links-row">
              <ArrowLeft size={12} strokeWidth={2} className="links-arrow" />
              <button
                className="links-target"
                onClick={() => void openScene(back.chapterGuid, back.sceneId)}
              >
                {back.chapterTitle} - {back.sceneTitle}
              </button>
              {back.note && <span className="links-backnote">{back.note}</span>}
            </div>
          ))}
        </>
      )}
    </div>
  )
}
