import { useTranslation } from 'react-i18next'
import { GitCompare, MoreHorizontal, PenLine, Plus, Search, Trash2, Wand2 } from 'lucide-react'
import { runCommand } from './commands'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { chromeForView } from './modes'

/**
 * The open project's command bar.
 *
 * It used to carry the three panel toggles as well - binder, context sidebar
 * and scene notes - each of which was already an item in the View menu, and
 * the pane controls, and the scene's snapshot history. None of those act on
 * the project: two shape the window, which is the application's business and
 * so the menu bar's, and a snapshot is of the scene in front of the writer,
 * which makes it the writing view's.
 *
 * What is left all acts on the open project: which book and draft, what is in
 * them, and finding something in them.
 */
// placement-container: projectBar
export function Toolbar(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const shellCapacity = useShellStore((s) => s.shellCapacity)
  const projectName = useProjectStore((s) => s.projectName)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  const drafts = useProjectStore((s) => s.drafts)
  const chapters = useProjectStore((s) => s.chapters)
  const isMac = window.novalist.platform === 'darwin'

  const activeDraft = drafts.find((d) => d.isActive) ?? null
  const chrome = chromeForView(mainView)
  const wide = shellCapacity === 'wide'
  const compact = shellCapacity === 'compact'
  const showSelectors = isLoaded && chrome.projectBar && !compact

  const bookSelect = (
    <select
      className="toolbar-select"
      data-command="project.newBook"
      value={activeBookId ?? ''}
      onChange={(e) => {
        if (e.target.value === '__new__') runCommand('project.newBook')
        else void useProjectStore.getState().switchBook(e.target.value)
      }}
    >
      {books.map((book) => (
        <option key={book.id} value={book.id}>
          {book.name}
        </option>
      ))}
      <option value="__new__">{t('book.addBook')}</option>
    </select>
  )

  const draftSelect = (
    <select
      className="toolbar-select"
      data-command="project.newDraft"
      value={activeDraft?.id ?? ''}
      onChange={(e) => {
        if (e.target.value === '__new__') runCommand('project.newDraft')
        else void useProjectStore.getState().switchDraft(e.target.value)
      }}
    >
      {drafts.map((draft) => (
        <option key={draft.id} value={draft.id}>
          {draft.name}
        </option>
      ))}
      <option value="__new__">{t('draft.add')}</option>
    </select>
  )

  return (
    <header className={`toolbar${isMac ? ' toolbar-mac' : ''}`}>
      {/* The burger that opened a backstage drawer is gone. Apart from the
          recent-projects list - which is now File > Recent projects, where a
          reader of any other application would look for it - the drawer was a
          second copy of the File and Help menus, built when there was no
          visible menu bar to put them in. */}
      <button
        className="toolbar-book"
        data-command="project.rename"
        title={t('command.renameProject')}
        onDoubleClick={() => projectName && runCommand('project.rename')}
      >
        {projectName ?? 'Novalist'}
      </button>
      {showSelectors && books.length > 0 && bookSelect}
      {showSelectors && drafts.length > 0 && (
        <div className="toolbar-draft">
          {draftSelect}
          {wide && (
            <>
              <button
                className="toolbar-button"
                data-command="project.compareDrafts"
                title={t('draftCompare.title')}
                disabled={drafts.length < 2}
                onClick={() => runCommand('project.compareDrafts')}
              >
                <GitCompare size={14} strokeWidth={1.75} />
              </button>
              <button
                className="toolbar-button"
                data-command="project.deleteDraft"
                title={t('draft.deleteTitle')}
                disabled={drafts.length <= 1 || !activeDraft}
                onClick={() => runCommand('project.deleteDraft')}
              >
                <Trash2 size={14} strokeWidth={1.75} />
              </button>
            </>
          )}
        </div>
      )}
      {/* Everything past the wordmark acts on an open project - adding a chapter
          or scene, or searching it. The spacer stays either way, so the strip
          keeps the drag region macOS still needs behind it. */}
      {isLoaded && chrome.projectBar && (
        <>
          {!compact && (
            <button
              className="toolbar-button toolbar-action"
              data-command="project.newChapter"
              onClick={() => runCommand('project.newChapter')}
            >
              <Plus size={14} strokeWidth={2} />
              {t('shell.newChapter')}
            </button>
          )}
          <button
            className="toolbar-button toolbar-action"
            data-command="project.newScene"
            disabled={chapters.length === 0}
            onClick={() => runCommand('project.newScene')}
          >
            <Plus size={14} strokeWidth={2} />
            {t('shell.newScene')}
          </button>
        </>
      )}
      <div className="toolbar-spacer" />
      {isLoaded && wide && chrome.projectBar && (
        <button
          className="toolbar-button"
          data-command="project.findReplace"
          title={t('findReplace.title')}
          onClick={() => runCommand('project.findReplace')}
        >
          <Search size={15} strokeWidth={1.75} />
        </button>
      )}
      {/* The project bar's overflow. It used to appear only when the window was
          too narrow for the buttons, which meant the commands that never had a
          button - cleaning up the manuscript, renaming the project - had no
          home at all and could be reached only by name. */}
      {isLoaded && chrome.projectBar && (
        <details className="toolbar-more">
          <summary className="toolbar-button" aria-label={t('shell.more')}>
            <MoreHorizontal size={16} strokeWidth={1.75} />
            <span>{t('shell.more')}</span>
          </summary>
          <div className="toolbar-more-menu">
            {/* What is up on the strip at this width is not repeated down here. */}
            {compact && books.length > 0 && (
              <label className="toolbar-more-field">
                <span>{t('book.label')}</span>
                {bookSelect}
              </label>
            )}
            {compact && drafts.length > 0 && (
              <label className="toolbar-more-field">
                <span>{t('draft.label')}</span>
                {draftSelect}
              </label>
            )}
            {compact && (
              <button
                className="toolbar-more-action"
                onClick={() => runCommand('project.newChapter')}
              >
                <Plus size={15} strokeWidth={1.75} />
                {t('shell.newChapter')}
              </button>
            )}
            {!wide && (
              <button
                className="toolbar-more-action"
                onClick={() => runCommand('project.findReplace')}
              >
                <Search size={15} strokeWidth={1.75} />
                {t('findReplace.title')}
              </button>
            )}
            <button
              className="toolbar-more-action"
              data-command="project.cleanup"
              onClick={() => runCommand('project.cleanup')}
            >
              <Wand2 size={15} strokeWidth={1.75} />
              {t('cleanup.title')}
            </button>
            <button className="toolbar-more-action" onClick={() => runCommand('project.rename')}>
              <PenLine size={15} strokeWidth={1.75} />
              {t('command.renameProject')}
            </button>
            {!wide && drafts.length > 0 && (
              <>
                <button
                  className="toolbar-more-action"
                  disabled={drafts.length < 2}
                  onClick={() => runCommand('project.compareDrafts')}
                >
                  <GitCompare size={15} strokeWidth={1.75} />
                  {t('draftCompare.title')}
                </button>
                <button
                  className="toolbar-more-action"
                  disabled={drafts.length <= 1 || !activeDraft}
                  onClick={() => runCommand('project.deleteDraft')}
                >
                  <Trash2 size={15} strokeWidth={1.75} />
                  {t('draft.deleteTitle')}
                </button>
              </>
            )}
          </div>
        </details>
      )}
      {/* The dialogs these buttons raise are rendered by ShellDialogs, which
          the shell owns - so the palette and the menu bar raise exactly the
          same ones rather than each toolbar holding its own copy. */}
      {/* placement-container: end */}
    </header>
  )
}
