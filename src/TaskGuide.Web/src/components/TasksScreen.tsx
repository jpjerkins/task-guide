import { useCallback, useEffect, useState } from 'react'
import { createTask, fetchTasks } from '../api/client'
import type { Task } from '../api/types'
import { QuickAdd } from './QuickAdd'

type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; tasks: Task[] }

function durLabel(minutes: number) {
  return `${minutes}m`
}

export function TasksScreen() {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [sheetOpen, setSheetOpen] = useState(false)

  const load = useCallback(async () => {
    setState({ status: 'loading' })
    try {
      const tasks = await fetchTasks()
      setState({ status: 'ready', tasks })
    } catch {
      setState({ status: 'error' })
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function handleAdd(title: string, duration: number) {
    await createTask({ title, duration })
    setSheetOpen(false)
    await load()
  }

  return (
    <div className="nav">
      <div className="nav-main">
        <h1>Tasks</h1>
        <button
          className="nav-add"
          aria-label="Quick add a task"
          onClick={() => setSheetOpen(true)}
        >
          +
        </button>
      </div>
      <div className="scroll">
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn't load tasks. Check your connection and try again.</div>
        )}
        {state.status === 'ready' && (
          <div className="list">
            {state.tasks.length === 0 ? (
              <div className="empty">Nothing here.</div>
            ) : (
              state.tasks.map((t) => (
                <div className="row" key={t.id}>
                  <div className="body">
                    <div className="title">{t.title}</div>
                    <div className="meta">
                      <span className="pill dur">{durLabel(t.duration)}</span>
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        )}
      </div>
      {sheetOpen && <QuickAdd onCancel={() => setSheetOpen(false)} onAdd={handleAdd} />}
    </div>
  )
}
