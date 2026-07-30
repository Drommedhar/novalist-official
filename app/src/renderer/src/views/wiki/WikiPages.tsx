import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FilePlus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { MarkdownEditor } from '../../shell/MarkdownEditor'

export interface WikiPage {
  id: string
  title: string
  parentId: string
  body: string
  order: number
  updatedAt: string
}

/** Children of one page, in order. */
function childrenOf(all: WikiPage[], parentId: string): WikiPage[] {
  return all
    .filter((p) => p.parentId === parentId)
    .sort(
      (a, b) =>
        a.order - b.order ||
        a.title.localeCompare(b.title, undefined, { sensitivity: 'base' })
    )
}

/**
 * The index side: free-form articles, nested to any depth.
 *
 * Every Wiki article was generated from a Codex entity, so an essay on how the
 * economy works had to hang off whichever entity it least badly belonged to —
 * or live in Research, outside the Wiki entirely.
 */
export function WikiPageIndex(props: {
  pages: WikiPage[]
  currentId: string | null
  onOpen: (id: string) => void
  onCreate: (parentId: string) => void
}): React.JSX.Element {
  const { t } = useTranslation()

  const branch = (parentId: string, depth: number): React.JSX.Element[] =>
    childrenOf(props.pages, parentId).flatMap((page) => [
      <li key={page.id}>
        <button
          type="button"
          className={`wiki-entry wiki-page-entry${props.currentId === page.id ? ' active' : ''}`}
          style={{ paddingInlineStart: `calc(var(--nl-space-sm) * ${depth + 1})` }}
          onClick={() => props.onOpen(page.id)}
        >
          <span className="wiki-entry-text">
            <span className="wiki-entry-title">
              {page.title || t('pages.untitled')}
            </span>
          </span>
        </button>
      </li>,
      ...branch(page.id, depth + 1)
    ])

  return (
    <div className="wiki-type-group">
      <div className="wiki-type-label">
        {t('pages.group')}
        <button
          type="button"
          className="binder-row-action"
          aria-label={t('pages.create')}
          title={t('pages.create')}
          onClick={() => props.onCreate('')}
        >
          <FilePlus size={12} strokeWidth={2} />
        </button>
      </div>
      <ul className="wiki-entry-list">{branch('', 0)}</ul>
    </div>
  )
}

/**
 * The article side: the page itself, editable in place.
 *
 * Unlike an entity article, which is generated, this one is written — so it is
 * edited where it is read rather than in a dialog somewhere else.
 */
export function WikiPageArticle(props: {
  page: WikiPage
  pages: WikiPage[]
  onChanged: (pages: WikiPage[]) => void
  onClosed: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [title, setTitle] = useState(props.page.title)
  const [body, setBody] = useState(props.page.body)

  useEffect(() => {
    setTitle(props.page.title)
    setBody(props.page.body)
  }, [props.page.id, props.page.title, props.page.body])

  const save = useCallback(
    (nextTitle: string, nextBody: string, parentId: string) => {
      void rpc
        .request<WikiPage[]>('pages/save', [props.page.id, nextTitle, nextBody, parentId])
        .then(props.onChanged)
    },
    [props.page.id, props.onChanged]
  )

  // A page cannot be filed under itself or under anything below it, so those
  // are not offered rather than being offered and refused.
  const descendants = (id: string): string[] =>
    childrenOf(props.pages, id).flatMap((c) => [c.id, ...descendants(c.id)])
  const forbidden = new Set([props.page.id, ...descendants(props.page.id)])

  return (
    <article className="wiki-article wiki-page-article">
      <input
        className="wiki-page-title"
        value={title}
        placeholder={t('pages.titlePlaceholder')}
        onChange={(e) => setTitle(e.target.value)}
        onBlur={() => save(title, body, props.page.parentId)}
      />

      <div className="wiki-page-toolbar">
        <label className="inspector-label" htmlFor="wiki-page-parent">
          {t('pages.parent')}
        </label>
        <select
          id="wiki-page-parent"
          className="inspector-input"
          value={props.page.parentId}
          onChange={(e) => save(title, body, e.target.value)}
        >
          <option value="">{t('pages.topLevel')}</option>
          {props.pages
            .filter((p) => !forbidden.has(p.id))
            .map((p) => (
              <option key={p.id} value={p.id}>
                {p.title || t('pages.untitled')}
              </option>
            ))}
        </select>
        <span className="toolbar-spacer" />
        <button
          type="button"
          className="btn-secondary"
          onClick={() => rpc.request<WikiPage[]>('pages/save', [null, '', '', props.page.id]).then(props.onChanged)}
        >
          <FilePlus size={12} strokeWidth={2} /> {t('pages.createChild')}
        </button>
        {/* Children move up to where this page was rather than vanishing with
            it: a page is a container as much as an article. */}
        <button
          type="button"
          className="binder-row-action"
          aria-label={t('pages.delete')}
          title={t('pages.delete')}
          onClick={() =>
            void rpc.request<WikiPage[]>('pages/delete', [props.page.id]).then((next) => {
              props.onChanged(next)
              props.onClosed()
            })
          }
        >
          <Trash2 size={12} strokeWidth={2} />
        </button>
      </div>

      {/* The same formatted-text editor a Codex section uses, so an essay is
          written the way every other long field in the app is. */}
      <MarkdownEditor
        value={body}
        minRows={16}
        placeholder={t('pages.bodyPlaceholder')}
        ariaLabel={t('pages.body')}
        onChange={setBody}
        onBlur={() => save(title, body, props.page.parentId)}
      />
    </article>
  )
}
