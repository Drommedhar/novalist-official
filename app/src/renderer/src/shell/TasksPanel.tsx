import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, RotateCcw, Trash2 } from 'lucide-react'
import { rpc } from '../rpc/client'

interface ProjectTask {
  id: string
  text: string
  list: string
  done: boolean
  doneAt: string | null
  chapterGuid: string
  sceneId: string
  order: number
}

/**
 * Things to do before the book is finished.
 *
 * Todo comments are anchored to a passage and belong to the scene they sit in.
 * "Check the dates in act two", "read the whole thing aloud", "decide whether
 * Tomas survives" belong to no passage and no scene, so they were kept on
 * paper.
 */
export function TasksPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [tasks, setTasks] = useState<ProjectTask[]>([])
  const [text, setText] = useState('')
  const [list, setList] = useState('')

  const load = useCallback(() => {
    void rpc
      .request<ProjectTask[]>('tasks/list')
      .then(setTasks)
      .catch(() => setTasks([]))
  }, [])

  useEffect(load, [load])

  const add = (): void => {
    if (text.trim().length === 0) return
    void rpc.request<ProjectTask[]>('tasks/save', [null, text, list]).then((next) => {
      setTasks(next)
      setText('')
    })
  }

  // Named lists first, the loose pile last: a checklist is a thing being
  // worked through, and a stray note is not.
  const lists = [...new Set(tasks.map((task) => task.list))].sort((a, b) =>
    a === '' ? 1 : b === '' ? -1 : a.localeCompare(b, undefined, { sensitivity: 'base' })
  )

  return (
    <div className="tasks-panel">
      <div className="inspector-label">{t('tasks.title')}</div>

      <div className="tasks-add">
        <input
          className="dialog-input"
          placeholder={t('tasks.textPlaceholder')}
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        <input
          className="inspector-input tasks-list-name"
          placeholder={t('tasks.listPlaceholder')}
          value={list}
          list="nl-task-lists"
          onChange={(e) => setList(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        {/* Offers the names already in use, so a checklist is not split in two
            by a capital letter. */}
        <datalist id="nl-task-lists">
          {lists.filter((name) => name.length > 0).map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <button className="btn-secondary" disabled={text.trim().length === 0} onClick={add}>
          <Plus size={12} strokeWidth={2} />
        </button>
      </div>

      {tasks.length === 0 && <p className="settings-hint">{t('tasks.empty')}</p>}

      {lists.map((name) => {
        const inList = tasks.filter((task) => task.list === name)
        const doneCount = inList.filter((task) => task.done).length
        return (
          <div key={name || '_loose'} className="task-list">
            <div className="task-list-head">
              <span className="task-list-name">{name || t('tasks.loose')}</span>
              <span className="inspector-meta">
                {t('tasks.progress', { done: doneCount, total: inList.length })}
              </span>
              {/* A checklist is run once per revision pass. Retyping it every
                  time is how it stops being used. */}
              {name.length > 0 && doneCount > 0 && (
                <button
                  className="binder-row-action"
                  aria-label={t('tasks.reset')}
                  title={t('tasks.reset')}
                  onClick={() =>
                    void rpc.request<ProjectTask[]>('tasks/resetList', [name]).then(setTasks)
                  }
                >
                  <RotateCcw size={12} strokeWidth={2} />
                </button>
              )}
            </div>

            {inList.map((task) => (
              <div key={task.id} className={`task${task.done ? ' done' : ''}`}>
                <label className="relationships-toggle">
                  <input
                    type="checkbox"
                    checked={task.done}
                    onChange={(e) =>
                      void rpc
                        .request<ProjectTask[]>('tasks/setDone', [task.id, e.target.checked])
                        .then(setTasks)
                    }
                  />
                  <span className="task-text">{task.text}</span>
                </label>
                <button
                  className="binder-row-action"
                  aria-label={t('tasks.remove')}
                  title={t('tasks.remove')}
                  onClick={() =>
                    void rpc.request<ProjectTask[]>('tasks/remove', [task.id]).then(setTasks)
                  }
                >
                  <Trash2 size={12} strokeWidth={2} />
                </button>
              </div>
            ))}
          </div>
        )
      })}
    </div>
  )
}
