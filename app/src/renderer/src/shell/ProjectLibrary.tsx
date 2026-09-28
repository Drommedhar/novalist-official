import { useEffect, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ArchiveRestore, ArrowDown, ArrowUp, BookOpen, CircleHelp, FilePlus2, FolderOpen, Import, LibraryBig, MoreHorizontal, Plus, Search, Settings, Trash2 } from 'lucide-react'
import { runCommand } from './commands'
import { LibraryScratchpad } from './LibraryScratchpad'
import { projectKey, useBookshelfStore } from '../stores/bookshelfStore'
import type { RecentProjectDto } from '../stores/projectStore'
import './bookshelf.css'

const PROJECT_DRAG = 'application/x-novalist-project'
interface StartScreenProps { recentProjects: RecentProjectDto[]; onOpenPath(path: string, bookId?: string): void }

export function StartScreen({ recentProjects, onOpenPath }: StartScreenProps): React.JSX.Element {
  const { t } = useTranslation()
  const library = useBookshelfStore()
  useEffect(() => {
    useBookshelfStore.getState().registerProjects(recentProjects.map(projectKey))
  }, [recentProjects])
  const [search, setSearch] = useState('')
  const [newShelf, setNewShelf] = useState<string | null>(null)
  const [dropShelf, setDropShelf] = useState<string | null>(null)
  const [failedCovers, setFailedCovers] = useState<Set<string>>(new Set())
  const needle = search.trim().toLocaleLowerCase()
  const assigned = new Set(library.shelves.flatMap((s) => s.projects))
  const rowById = new Map([{ id: '', name: t('bookshelf.unshelved'), projects: library.unshelved }, ...library.shelves].map((shelf) => [shelf.id, shelf]))
  const rows = library.shelfOrder.map((id) => rowById.get(id)!).filter(Boolean)
  const visible = recentProjects.filter((p) => !needle ||
    [p.name, ...(p.books ?? []).map((book) => book.name)].some((value) => value.toLocaleLowerCase().includes(needle)))
  const accept = (event: DragEvent, shelf: string, before?: string): void => {
    const key = event.dataTransfer.getData(PROJECT_DRAG)
    if (!recentProjects.some((p) => projectKey(p) === key)) return
    event.preventDefault(); event.stopPropagation()
    library.moveProject(key, shelf, before); setDropShelf(null)
  }

  return <div className="start-screen project-library">
    <div className="library-content">
      <header className="library-header">
        <div className="library-brand"><BookOpen size={32} strokeWidth={1.25} /><span>Novalist</span></div>
        <div className="library-actions">
          <button className="start-open" onClick={() => runCommand('app.newProject')}><FilePlus2 size={17} /> {t('welcome.newProject')}</button>
          <button className="start-open secondary" onClick={() => runCommand('app.openProject')}><FolderOpen size={17} /> {t('welcome.browseFolder')}</button>
          <details className="library-more">
            <summary aria-label={t('bookshelf.more')} title={t('bookshelf.more')}><MoreHorizontal size={22} /></summary>
            <div className="library-more-menu">
              <button onClick={() => runCommand('app.importProject')}><Import size={16} /> {t('welcome.importPlugin')}</button>
              <button onClick={() => runCommand('app.restoreBackup')}><ArchiveRestore size={16} /> {t('backup.restoreAsNew')}</button>
              <button onClick={() => runCommand('nav.settings')}><Settings size={16} /> {t('settings.title')}</button>
              <button onClick={() => runCommand('app.manual')}><CircleHelp size={16} /> {t('help.title')}</button>
            </div>
          </details>
        </div>
      </header>
      <section className="library-intro">
        <div><p className="library-eyebrow">{t('bookshelf.eyebrow')}</p><h1>{t('bookshelf.title')}</h1><p>{t('bookshelf.description')}</p></div>
        <span className="library-total">{t('bookshelf.projects', { count: recentProjects.length })}</span>
      </section>
      <div className="library-controls">
        <label className="library-search"><Search size={17} /><input value={search} placeholder={t('bookshelf.search')}
          aria-label={t('bookshelf.search')} onChange={(e) => setSearch(e.target.value)} /></label>
        <LibraryScratchpad />
        <button className="library-add-shelf" onClick={() => setNewShelf('')}><Plus size={17} /> {t('bookshelf.addShelf')}</button>
      </div>
      {newShelf !== null && <form className="library-shelf-form" onSubmit={(event) => {
        event.preventDefault(); if (!newShelf.trim()) return
        library.addShelf(newShelf); setNewShelf(null)
      }}>
        <input autoFocus aria-label={t('bookshelf.shelfName')} placeholder={t('bookshelf.shelfName')}
          value={newShelf} onChange={(event) => setNewShelf(event.target.value)} />
        <button type="submit" disabled={!newShelf.trim()}>{t('bookshelf.createShelf')}</button>
        <button type="button" onClick={() => setNewShelf(null)}>{t('bookshelf.cancel')}</button>
      </form>}
      {recentProjects.length === 0 && <div className="library-empty"><LibraryBig size={42} strokeWidth={1} /><p>{t('bookshelf.empty')}</p></div>}
      {needle && visible.length === 0 && <p className="library-no-results">{t('bookshelf.noResults')}</p>}
      {rows.map((shelf, rowIndex) => {
        const projects = visible.filter((p) => shelf.id ? shelf.projects.includes(projectKey(p)) : !assigned.has(projectKey(p)))
        projects.sort((a, b) => {
          const aIndex = shelf.projects.indexOf(projectKey(a)), bIndex = shelf.projects.indexOf(projectKey(b))
          return (aIndex < 0 ? Number.MAX_SAFE_INTEGER : aIndex) - (bIndex < 0 ? Number.MAX_SAFE_INTEGER : bIndex)
        })
        if (needle && projects.length === 0) return null
        return <section key={shelf.id} className={`project-shelf${dropShelf === shelf.id ? ' drag-over' : ''}`}
          aria-label={shelf.name} onDragOver={(event) => {
            if (event.dataTransfer.types.includes(PROJECT_DRAG)) { event.preventDefault(); setDropShelf(shelf.id) }
          }} onDragLeave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDropShelf(null)
          }} onDrop={(event) => accept(event, shelf.id)}>
          <div className="project-shelf-heading">
            {shelf.id ? <input className="shelf-name" key={shelf.name} defaultValue={shelf.name}
              aria-label={t('bookshelf.renameShelf', { name: shelf.name })}
              onBlur={(event) => { library.renameShelf(shelf.id, event.target.value); event.target.value = event.target.value.trim() || shelf.name }}
              onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur(); if (event.key === 'Escape') { event.currentTarget.value = shelf.name; event.currentTarget.blur() } }} />
              : <h2>{shelf.name}</h2>}
            <span className="shelf-count">{t('bookshelf.projects', { count: projects.length })}</span>
            <div className="shelf-actions">
              <button disabled={rowIndex === 0} aria-label={t('bookshelf.moveShelfUp')} onClick={() => library.moveShelf(shelf.id, -1)}><ArrowUp size={16} /></button>
              <button disabled={rowIndex === rows.length - 1} aria-label={t('bookshelf.moveShelfDown')} onClick={() => library.moveShelf(shelf.id, 1)}><ArrowDown size={16} /></button>
              {shelf.id && <button aria-label={t('bookshelf.removeShelf')} title={t('bookshelf.removeShelfHint')} onClick={() => library.removeShelf(shelf.id)}><Trash2 size={16} /></button>}
            </div>
          </div>
          <div className="project-shelf-row">
            {projects.map((project, projectIndex) => {
              const key = projectKey(project)
              const books = project.books?.length ? project.books : [{ id: undefined, name: project.name, cover: project.cover }]
              const matchingBooks = books.filter((book) => !needle || project.name.toLocaleLowerCase().includes(needle) || book.name.toLocaleLowerCase().includes(needle))
              return <article key={key} className="project-volume" draggable
                aria-label={project.name}
                onDragStart={(event) => { event.dataTransfer.setData(PROJECT_DRAG, key); event.dataTransfer.effectAllowed = 'move' }}
                onDragEnd={() => setDropShelf(null)} onDrop={(event) => accept(event, shelf.id, key)}>
                <div className="library-project-heading">
                  <button className="library-project-title" onClick={() => onOpenPath(project.path)} title={project.path}
                    aria-label={t('bookshelf.openProject', { name: project.name })}>
                    <span className="start-recent-name">{project.name}</span>
                    <span className="volume-details">{project.books ? t('bookshelf.books', { count: project.books.length }) : t('bookshelf.project')}
                      {project.hasWorldBible && <> · {t('bookshelf.worldBible')}</>}</span>
                  </button>
                  <div className="library-project-actions">
                    <select className="volume-shelf-select" value={shelf.id} aria-label={t('bookshelf.moveProject', { name: project.name })}
                      onChange={(event) => library.moveProject(key, event.target.value)}>
                      {rows.map((row) => <option key={row.id} value={row.id}>{row.name}</option>)}
                    </select>
                    <div className="volume-order">
                      <button disabled={projectIndex === 0} aria-label={t('bookshelf.earlier', { name: project.name })}
                        onClick={() => library.moveProject(key, shelf.id, projectKey(projects[projectIndex - 1]))}><ArrowUp size={14} /></button>
                      <button disabled={projectIndex === projects.length - 1} aria-label={t('bookshelf.later', { name: project.name })}
                        onClick={() => library.moveProject(key, shelf.id, projects[projectIndex + 2] && projectKey(projects[projectIndex + 2]))}><ArrowDown size={14} /></button>
                    </div>
                  </div>
                </div>
                <div className="project-books">
                  {matchingBooks.map((book) => {
                    const ornament = [...book.name].reduce((hash, char) => hash + char.charCodeAt(0), 0) % 3
                    return <div className="library-book" key={book.id ?? key}>
                      <button className="start-recent-card" onClick={() => onOpenPath(project.path, book.id)}
                        aria-label={book.id ? t('bookshelf.openBook', { book: book.name, project: project.name }) : t('bookshelf.openProject', { name: project.name })}>
                        <div className={`start-recent-cover volume-cover ornament-${ornament}`}>
                          {book.cover && !failedCovers.has(book.cover) ?
                            <img className="start-recent-cover-img" src={book.cover} alt="" draggable={false}
                              onError={() => setFailedCovers((old) => new Set(old).add(book.cover!))} /> :
                            <div className="volume-cover-lettering"><span className="volume-imprint">NOVALIST</span>
                              <span className="volume-title">{book.name}</span><span className="volume-rule" />
                              <BookOpen size={24} strokeWidth={1} /><span className="volume-imprint">{t(book.id ? 'bookshelf.book' : 'bookshelf.project')}</span></div>}
                        </div>
                        <span className="volume-name">{book.name}</span>
                      </button>
                    </div>
                  })}
                </div>
              </article>
            })}
            {projects.length === 0 && <p className="shelf-empty">{t('bookshelf.emptyShelf')}</p>}
          </div>
        </section>
      })}
    </div>
  </div>
}
