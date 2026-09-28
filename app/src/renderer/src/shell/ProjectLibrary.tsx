import { useEffect, useId, useRef, useState, type CSSProperties, type DragEvent, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ArchiveRestore, ArrowDown, ArrowRight, ArrowUp, BookOpen, CircleHelp, EyeOff, FolderOpen, Globe, Import, LibraryBig, MoreHorizontal, Plus, Search, Settings, SlidersHorizontal, Trash2, X } from 'lucide-react'
import { runCommand } from './commands'
import { LibraryScratchpad } from './LibraryScratchpad'
import { GeneratedBookCover } from './GeneratedBookCover'
import { RemovedLibraryItems } from './RemovedLibraryItems'
import { projectKey, useBookshelfStore, type LibrarySort, type RemovedLibraryItem } from '../stores/bookshelfStore'
import { useProjectStore, type RecentProjectDto } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import './bookshelf.css'

const PROJECT_DRAG = 'application/x-novalist-project'
interface LibraryBook { id?: string; name: string; cover?: string | null }
interface Selection { path: string; bookId?: string }
interface SelectionClick { target: HTMLButtonElement; project: RecentProjectDto; book: LibraryBook; x: number; y: number; time: number }
interface StartScreenProps { recentProjects: RecentProjectDto[]; onOpenPath(path: string, bookId?: string): Promise<void> }
const projectBooks = (project: RecentProjectDto): LibraryBook[] => project.books?.length ? project.books : [{ name: project.name, cover: project.cover }]

export function StartScreen({ recentProjects, onOpenPath }: StartScreenProps): React.JSX.Element {
  const { t } = useTranslation()
  const library = useBookshelfStore()
  const [search, setSearch] = useState('')
  const [activeShelf, setActiveShelf] = useState<string | null>(null)
  const [newShelf, setNewShelf] = useState<string | null>(null)
  const [renameShelf, setRenameShelf] = useState<string | null>(null)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [dropShelf, setDropShelf] = useState<string | null>(null)
  const [failedCovers, setFailedCovers] = useState<Set<string>>(new Set())
  const [removedOpen, setRemovedOpen] = useState(false)
  const [lastRemoved, setLastRemoved] = useState<RemovedLibraryItem | null>(null)
  const [opening, setOpening] = useState(false)
  const [openError, setOpenError] = useState<string | null>(null)
  const undoButton = useRef<HTMLButtonElement>(null)
  const searchInput = useRef<HTMLInputElement>(null)
  const moreTrigger = useRef<HTMLElement>(null)
  const openButton = useRef<HTMLButtonElement>(null)
  const selectedTrigger = useRef<HTMLButtonElement | null>(null)
  const selectionClick = useRef<SelectionClick | null>(null)
  const instructionsId = useId()
  useEffect(() => { library.registerProjects(recentProjects.map(projectKey)) }, [recentProjects, library.registerProjects])
  useEffect(() => { if (lastRemoved) undoButton.current?.focus() }, [lastRemoved])
  useEffect(() => { if (activeShelf && !library.shelves.some(shelf => shelf.id === activeShelf)) setActiveShelf(null) }, [activeShelf, library.shelves])
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (document.querySelector('dialog[open], [aria-modal="true"]')) return
      const input = event.target instanceof Element && event.target.closest('input, textarea, select, [contenteditable="true"]')
      if (event.key === '/' && !input && !event.ctrlKey && !event.altKey && !event.metaKey) { event.preventDefault(); searchInput.current?.focus() }
      if (event.key === 'Escape') { selectionClick.current = null; if (selection) { event.preventDefault(); setSelection(null); selectedTrigger.current?.focus() } }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [selection])

  const needle = search.trim().toLocaleLowerCase()
  const assigned = new Set(library.shelves.flatMap(shelf => shelf.projects))
  const rowById = new Map([{ id: '', name: t('bookshelf.unshelved'), projects: library.unshelved }, ...library.shelves].map(shelf => [shelf.id, shelf]))
  const rows = library.shelfOrder.map(id => rowById.get(id)!).filter(Boolean)
  const availableProjects = recentProjects.flatMap(project => {
    const removed = library.removed.filter(item => item.projectKey === projectKey(project))
    if (removed.some(item => !item.bookId)) return []
    const books = project.books?.filter(book => !removed.some(item => item.bookId === book.id))
    if (project.books?.length && !books?.length) return []
    return [{ ...project, books }]
  })
  const shelfFor = (project: RecentProjectDto): string => rows.find(row => row.id && row.projects.includes(projectKey(project)))?.id ?? ''
  const selectedProject = selection && availableProjects.find(project => project.path === selection.path)
  const selectedBook = selectedProject && projectBooks(selectedProject).find(book => book.id === selection?.bookId)
  const selectedShelf = selectedProject ? shelfFor(selectedProject) : ''
  const orderedProjects = (id: string): RecentProjectDto[] => {
    const projects = availableProjects.filter(project => id ? rowById.get(id)?.projects.includes(projectKey(project)) : !assigned.has(projectKey(project)))
    if (library.sort === 'title') return projects.sort((a, b) => projectBooks(a)[0].name.localeCompare(projectBooks(b)[0].name))
    if (library.sort === 'recent') return projects
    const order = rowById.get(id)?.projects ?? []
    const rank = (project: RecentProjectDto): number => { const index = order.indexOf(projectKey(project)); return index < 0 ? Number.MAX_SAFE_INTEGER : index }
    return projects.sort((a, b) => rank(a) - rank(b))
  }
  const visibleShelves = rows.filter(row => activeShelf === null || row.id === activeShelf).map(row => ({ ...row,
    projects: orderedProjects(row.id).filter(project => !needle || [project.name, ...projectBooks(project).map(book => book.name)].some(name => name.toLocaleLowerCase().includes(needle)))
  })).filter(row => !needle || row.projects.length > 0)
  const selectedNeighbors = selectedProject ? orderedProjects(selectedShelf) : []
  const selectedIndex = selectedProject ? selectedNeighbors.findIndex(project => project.path === selectedProject.path) : -1
  const remove = (item: RemovedLibraryItem): void => { library.removeItem(item); setLastRemoved(item); setSelection(null) }
  const accept = (event: DragEvent, shelf: string, before?: string): void => {
    const key = event.dataTransfer.getData(PROJECT_DRAG)
    if (!availableProjects.some(project => projectKey(project) === key)) return
    event.preventDefault(); event.stopPropagation(); library.moveProject(key, shelf, before); setDropShelf(null)
  }
  const open = async (project: RecentProjectDto, book: LibraryBook, view?: 'codex' | 'series'): Promise<void> => {
    if (opening) return
    setOpening(true); setOpenError(null)
    try { await onOpenPath(project.path, book.id); if (view && useProjectStore.getState().isLoaded) useShellStore.getState().setMainView(view) }
    catch (error) { setOpenError(String(error)) }
    finally { setOpening(false) }
  }
  const finishSelectionClick = (event: MouseEvent<HTMLDivElement>): void => {
    const first = selectionClick.current
    selectionClick.current = null
    if (!first || event.detail === 0 || event.button !== 0 || event.timeStamp - first.time > 500 ||
      Math.hypot(event.clientX - first.x, event.clientY - first.y) > 6 ||
      !first.target.isConnected || first.target.getAttribute('aria-pressed') !== 'true') return
    const bounds = first.target.getBoundingClientRect()
    if (event.clientX >= bounds.left && event.clientX <= bounds.right && event.clientY >= bounds.top && event.clientY <= bounds.bottom) return
    // Show details immediately. If that reflow moves the book away from the
    // pointer, a second click at its old position still opens the original book.
    // Clicks on a book that stayed under the pointer use native double-clicks.
    event.preventDefault()
    event.stopPropagation()
    void open(first.project, first.book)
  }
  const cover = (project: RecentProjectDto, book: LibraryBook): React.JSX.Element => <div className="start-recent-cover volume-cover">
    <div className="volume-cover-front">
      {book.cover && !failedCovers.has(book.cover)
        ? <img className="start-recent-cover-img" src={book.cover} alt="" draggable={false} onError={() => setFailedCovers(old => new Set(old).add(book.cover!))} />
        : <GeneratedBookCover projectSeed={project.projectId || project.path} bookSeed={book.id ?? 'project'} title={book.name} kind={t(book.id ? 'bookshelf.book' : 'bookshelf.project')} />}
    </div>
  </div>
  const closeMenu = (element: HTMLElement): void => { element.closest('details')?.removeAttribute('open') }

  return <div className="start-screen project-library" style={{ '--library-cover-scale': library.coverScale } as CSSProperties} onClickCapture={finishSelectionClick}>
    <div className="library-content">
      <header className="library-header">
        <div className="library-title"><h1>{t('bookshelf.yourBookshelf')}</h1><span className="library-total">{t('bookshelf.projects', { count: availableProjects.length })}</span></div>
        <div className="library-actions">
          <LibraryScratchpad compact />
          <button className="library-button library-open-folder" onClick={() => runCommand('app.openProject')} title={t('welcome.browseFolder')}><FolderOpen size={17} /><span>{t('bookshelf.openFolder')}</span></button>
          <button className="library-button library-primary" onClick={() => runCommand('app.newProject')}><Plus size={17} />{t('welcome.newProject')}</button>
          <details className="library-more"><summary ref={moreTrigger} aria-label={t('bookshelf.more')} title={t('bookshelf.more')}><MoreHorizontal size={20} /></summary>
            <div className="library-more-menu">
              <button onClick={event => { closeMenu(event.currentTarget); runCommand('app.importProject') }}><Import size={16} />{t('welcome.importPlugin')}</button>
              <button onClick={event => { closeMenu(event.currentTarget); runCommand('app.restoreBackup') }}><ArchiveRestore size={16} />{t('backup.restoreAsNew')}</button>
              <button onClick={event => { closeMenu(event.currentTarget); runCommand('nav.settings') }}><Settings size={16} />{t('settings.title')}</button>
              <button onClick={event => { closeMenu(event.currentTarget); runCommand('app.manual') }}><CircleHelp size={16} />{t('help.title')}</button>
              <button onClick={event => { closeMenu(event.currentTarget); setRemovedOpen(true) }}><EyeOff size={16} />{t('bookshelf.removedItems')}</button>
            </div>
          </details>
        </div>
      </header>
      <div className="library-controls">
        <nav className="library-filters" aria-label={t('bookshelf.filterShelves')}>
          <button aria-pressed={activeShelf === null} onClick={() => { setActiveShelf(null); setSelection(null) }}>{t('bookshelf.allShelves')}<small>{availableProjects.length}</small></button>
          {rows.map(row => <button key={row.id} aria-pressed={activeShelf === row.id} onClick={() => { setActiveShelf(row.id); setSelection(null) }}
            onDragOver={event => { if (event.dataTransfer.types.includes(PROJECT_DRAG)) event.preventDefault() }} onDrop={event => accept(event, row.id)}>
            {row.name}<small>{availableProjects.filter(project => shelfFor(project) === row.id).length}</small></button>)}
          <button className="library-add-shelf" aria-label={t('bookshelf.addShelf')} title={t('bookshelf.addShelf')} onClick={() => setNewShelf('')}><Plus size={17} /></button>
        </nav>
        <label className="library-search"><Search size={16} /><input ref={searchInput} type="search" value={search} placeholder={t('bookshelf.search')} aria-label={t('bookshelf.search')} onChange={event => { setSearch(event.target.value); setSelection(null) }} /></label>
        <label className="library-density" title={t('bookshelf.coverSize')}><SlidersHorizontal size={16} /><input type="range" min="70" max="120" step="5" value={Math.round(library.coverScale * 100)} aria-label={t('bookshelf.coverSize')} onChange={event => library.setCoverScale(Number(event.target.value) / 100)} /></label>
        <select className="library-sort" aria-label={t('bookshelf.sort')} value={library.sort} onChange={event => library.setSort(event.target.value as LibrarySort)}>
          <option value="shelf">{t('bookshelf.sortShelf')}</option><option value="recent">{t('bookshelf.sortRecent')}</option><option value="title">{t('bookshelf.sortTitle')}</option>
        </select>
      </div>
      {newShelf !== null && <form className="library-shelf-form" onSubmit={event => { event.preventDefault(); if (!newShelf.trim()) return; library.addShelf(newShelf); setNewShelf(null) }}>
        <input autoFocus aria-label={t('bookshelf.shelfName')} placeholder={t('bookshelf.shelfName')} value={newShelf} onChange={event => setNewShelf(event.target.value)} />
        <button className="library-button" type="submit" disabled={!newShelf.trim()}>{t('bookshelf.createShelf')}</button><button className="library-button" type="button" onClick={() => setNewShelf(null)}>{t('bookshelf.cancel')}</button></form>}
      {lastRemoved && <div className="library-removal-notice"><span role="status">{t('bookshelf.removedMessage', { name: lastRemoved.bookName ?? lastRemoved.projectName })}</span>
        <button ref={undoButton} onClick={() => { library.restoreItem(lastRemoved); setLastRemoved(null); searchInput.current?.focus() }}>{t('bookshelf.undoRemoval')}</button>
        <button aria-label={t('dialog.close')} onClick={() => { setLastRemoved(null); searchInput.current?.focus() }}><X size={16} /></button></div>}
      {openError && <p className="library-open-error" role="alert">{openError}</p>}
      <p id={instructionsId} className="library-sr-only">{t('bookshelf.selectionHint')}</p>
      <div className="library-workspace">
        <main className="library-shelves" aria-label={t('bookshelf.title')}>
          {availableProjects.length === 0 && <div className="library-empty"><LibraryBig size={48} strokeWidth={1} /><h2>{t('bookshelf.firstStory')}</h2><p>{t('bookshelf.empty')}</p><button className="library-button library-primary" onClick={() => runCommand('app.newProject')}><Plus size={17} />{t('welcome.newProject')}</button></div>}
          {availableProjects.length > 0 && needle && visibleShelves.length === 0 && <div className="library-empty"><Search size={32} /><p>{t('bookshelf.noResults')}</p><button className="library-button" onClick={() => setSearch('')}>{t('bookshelf.clearSearch')}</button></div>}
          {availableProjects.length > 0 && visibleShelves.map(shelf => {
            const rowIndex = rows.findIndex(row => row.id === shelf.id)
            return <section key={shelf.id} className={`project-shelf${dropShelf === shelf.id ? ' drag-over' : ''}`} aria-label={shelf.name}
              onDragOver={event => { if (event.dataTransfer.types.includes(PROJECT_DRAG)) { event.preventDefault(); setDropShelf(shelf.id) } }} onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDropShelf(null) }} onDrop={event => accept(event, shelf.id)}>
              <div className="project-shelf-heading">
                {renameShelf === shelf.id && shelf.id ? <input className="shelf-name" autoFocus defaultValue={shelf.name} aria-label={t('bookshelf.renameShelf', { name: shelf.name })}
                  onBlur={event => { library.renameShelf(shelf.id, event.target.value); setRenameShelf(null) }} onKeyDown={event => { if (event.key === 'Enter') event.currentTarget.blur(); if (event.key === 'Escape') { event.currentTarget.value = shelf.name; event.currentTarget.blur() } }} /> : <h2>{shelf.name}</h2>}
                <span className="shelf-count">{t('bookshelf.projects', { count: shelf.projects.length })}</span>
                <details className="library-more shelf-actions"><summary aria-label={t('bookshelf.shelfActions', { name: shelf.name })}><MoreHorizontal size={18} /></summary>
                  <div className="library-more-menu">
                    {shelf.id && <button onClick={event => { closeMenu(event.currentTarget); setRenameShelf(shelf.id) }}>{t('bookshelf.renameShelf', { name: shelf.name })}</button>}
                    <button disabled={rowIndex === 0} onClick={() => library.moveShelf(shelf.id, -1)}><ArrowUp size={16} />{t('bookshelf.moveShelfUp')}</button>
                    <button disabled={rowIndex === rows.length - 1} onClick={() => library.moveShelf(shelf.id, 1)}><ArrowDown size={16} />{t('bookshelf.moveShelfDown')}</button>
                    {shelf.id && <button title={t('bookshelf.removeShelfHint')} onClick={() => library.removeShelf(shelf.id)}><Trash2 size={16} />{t('bookshelf.removeShelf')}</button>}
                  </div>
                </details>
              </div>
              <div className="project-shelf-row">
                {shelf.projects.flatMap(project => projectBooks(project).filter(book => !needle || project.name.toLocaleLowerCase().includes(needle) || book.name.toLocaleLowerCase().includes(needle)).map(book => <div className="library-book" key={JSON.stringify([project.path, book.id])} data-project={project.path} draggable
                  onDragStart={event => { event.dataTransfer.setData(PROJECT_DRAG, project.path); event.dataTransfer.effectAllowed = 'move' }} onDragEnd={() => setDropShelf(null)} onDrop={event => accept(event, shelf.id, project.path)}>
                  <button className="start-recent-card" aria-label={t('bookshelf.selectBook', { name: book.name, project: project.name })} aria-pressed={selection?.path === project.path && selection.bookId === book.id} aria-describedby={instructionsId}
                    title={`${book.name} — ${project.name}`} onClick={event => {
                      selectedTrigger.current = event.currentTarget
                      if (!selectedProject && event.detail === 1) selectionClick.current = {
                        target: event.currentTarget, project, book, x: event.clientX, y: event.clientY, time: event.timeStamp
                      }
                      setSelection({ path: project.path, bookId: book.id })
                      if (event.detail === 0) requestAnimationFrame(() => openButton.current?.focus())
                    }} onDoubleClick={() => { selectionClick.current = null; void open(project, book) }}>
                    {cover(project, book)}<span className="volume-name library-sr-only">{book.name}</span>
                  </button>
                </div>))}
                {shelf.projects.length === 0 && <p className="shelf-empty">{t('bookshelf.emptyShelf')}</p>}
              </div>
            </section>
          })}
        </main>
        {selectedProject && selectedBook && <aside className="library-inspector" aria-label={t('bookshelf.bookDetails')}>
          <div className="library-inspector-heading"><span>{t(selectedBook.id ? 'bookshelf.selectedBook' : 'bookshelf.project')}</span><button aria-label={t('bookshelf.closeDetails')} onClick={() => { setSelection(null); selectedTrigger.current?.focus() }}><X size={18} /></button></div>
          <div className="library-detail-cover">{cover(selectedProject, selectedBook)}</div><h2>{selectedBook.name}</h2><p className="library-detail-project">{selectedProject.name}</p>
          <button ref={openButton} className="library-button library-primary library-open-book" disabled={opening} aria-label={selectedBook.id ? t('bookshelf.openBook', { book: selectedBook.name, project: selectedProject.name }) : t('bookshelf.openProject', { name: selectedProject.name })}
            onClick={() => { void open(selectedProject, selectedBook) }}>{t(selectedBook.id ? 'bookshelf.openSelectedBook' : 'bookshelf.openSelectedProject')}<ArrowRight size={17} /></button>
          <div className="library-related"><h3>{t('bookshelf.inProject')}</h3><p className="volume-details">{t('bookshelf.books', { count: selectedProject.books?.length ?? 0 })}{selectedProject.hasWorldBible && <> · {t('bookshelf.worldBible')}</>}</p>
            {projectBooks(selectedProject).map(book => <button key={book.id ?? 'project'} aria-current={book.id === selectedBook.id ? 'true' : undefined} onClick={() => setSelection({ path: selectedProject.path, bookId: book.id })}><BookOpen size={16} />{book.name}</button>)}
          </div>
          {selectedProject.hasWorldBible && <button className="library-button library-project-link" disabled={opening} onClick={() => { void open(selectedProject, selectedBook, 'codex') }}><Globe size={17} />{t('bookshelf.worldBible')}</button>}
          {(selectedProject.books?.length ?? 0) > 1 && <button className="library-button library-project-link" disabled={opening} onClick={() => { void open(selectedProject, selectedBook, 'series') }}><LibraryBig size={17} />{t('bookshelf.seriesOverview')}</button>}
          <label className="library-detail-label">{t('bookshelf.projectShelf')}<select className="volume-shelf-select" value={selectedShelf} aria-label={t('bookshelf.moveProject', { name: selectedProject.name })} onChange={event => { library.moveProject(selectedProject.path, event.target.value); if (activeShelf !== null) setActiveShelf(event.target.value) }}>
            {rows.map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>
          {(selectedProject.books?.length ?? 0) > 1 && <p className="library-move-hint">{t('bookshelf.moveTogether')}</p>}
          <div className="library-project-order"><button className="library-button" disabled={selectedIndex <= 0} aria-label={t('bookshelf.earlier', { name: selectedProject.name })} onClick={() => library.moveProject(selectedProject.path, selectedShelf, selectedNeighbors[selectedIndex - 1].path)}><ArrowUp size={16} />{t('bookshelf.earlierShort')}</button>
            <button className="library-button" disabled={selectedIndex === selectedNeighbors.length - 1} aria-label={t('bookshelf.later', { name: selectedProject.name })} onClick={() => library.moveProject(selectedProject.path, selectedShelf, selectedNeighbors[selectedIndex + 2]?.path)}><ArrowDown size={16} />{t('bookshelf.laterShort')}</button></div>
          <div className="library-remove-actions">{selectedBook.id && <button aria-label={t('bookshelf.removeBook', { name: selectedBook.name })} onClick={() => remove({ projectKey: selectedProject.path, projectName: selectedProject.name, bookId: selectedBook.id, bookName: selectedBook.name })}><EyeOff size={16} />{t('bookshelf.removeThisBook')}</button>}
            <button aria-label={t('bookshelf.removeProject', { name: selectedProject.name })} onClick={() => remove({ projectKey: selectedProject.path, projectName: selectedProject.name })}><EyeOff size={16} />{t('bookshelf.removeThisProject')}</button></div>
        </aside>}
      </div>
      <RemovedLibraryItems open={removedOpen} onClose={() => { setRemovedOpen(false); moreTrigger.current?.focus() }} recentProjects={recentProjects} />
    </div>
  </div>
}
