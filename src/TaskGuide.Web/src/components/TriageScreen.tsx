import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, fetchTasks, setTaskDuration, type Task } from '../api/client'
import { ScreenNav } from './shared/ScreenNav'

const UNPROCESSED_BUCKETS = ['2', '10', '30', '60', 'longer']

// Mirrors DurationCeiling.UnsizedBucketOf, same rule as TasksScreen.tsx's durLabel (copied, not
// imported — the two lanes don't share components).
function durLabel(bucket: string) {
  return Number.isNaN(Number(bucket)) ? 'Longer' : `${bucket}m`
}

// Same three-state shape as DimensionsScreen.tsx: a loading arm so the first paint doesn't say
// "nothing to triage" before the first read returns, and an error arm the ready counts can't leak
// past.
type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; unprocessed: Task[]; stale: Task[] }

// A duration write's failure note, scoped to the row that produced it — a later click on a
// *different* row must not clear it out from under the row it actually describes.
interface TaskActionNote {
  taskId: string
  text: string
}

export function TriageScreen() {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  // A Set, not a single slot: two rows' writes can be in flight at once, and a single slot would
  // drop row A's disabled guard the moment row B became "the" busy row.
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(new Set())
  const [taskActionNote, setTaskActionNote] = useState<TaskActionNote | null>(null)
  // Guards against an earlier `load()` call's response arriving after a later one's: only the
  // most recently issued call's result is applied.
  const loadToken = useRef(0)

  const load = useCallback(async () => {
    const token = ++loadToken.current
    try {
      const [unprocessed, stale] = await Promise.all([fetchTasks('unprocessed'), fetchTasks('stale')])
      if (loadToken.current !== token) return
      setState({ status: 'ready', unprocessed, stale })
    } catch {
      if (loadToken.current !== token) return
      setState({ status: 'error' })
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function handleDuration(taskId: string, bucket: string, title: string) {
    setTaskActionNote((prev) => (prev?.taskId === taskId ? null : prev))
    setBusyIds((prev) => new Set(prev).add(taskId))
    try {
      await setTaskDuration(taskId, bucket)
    } catch (err) {
      // Set before the reload, and rendered independently of the error arm below: offline, the
      // reload this triggers can fail too, and the note must survive that rather than being
      // dropped along with the ready-state body it would otherwise live inside. That's also why
      // this can't render inside the offending row: the error arm replaces the whole ready-state
      // body, rows included, so the row-scoped note would vanish along with it. The task's title
      // goes into the text instead, so the note still says which Task failed from the top of the
      // scroll area.
      // The server's own reason when it gave one (#138 put it on the wire), *appended to* the
      // sentence rather than replacing it: this note renders at the top of the scroll, so the
      // title is the only thing tying it to a row, and the reason names no Task. A failure with
      // no parsable body (offline, a bodyless 500) falls back to the sentence alone.
      const reason = err instanceof ApiError ? err.reason : null
      const failed = `Couldn't set the duration for "${title}"`
      setTaskActionNote({ taskId, text: reason ? `${failed} — ${reason}.` : `${failed}.` })
      await load()
      setBusyIds((prev) => {
        const next = new Set(prev)
        next.delete(taskId)
        return next
      })
      return
    }
    await load()
    setBusyIds((prev) => {
      const next = new Set(prev)
      next.delete(taskId)
      return next
    })
  }

  const sub =
    state.status === 'ready' ? `${state.unprocessed.length} unprocessed · ${state.stale.length} stale` : undefined

  return (
    <>
      <ScreenNav title="Process" sub={sub} />
      <div className="scroll">
        {taskActionNote && (
          <div className="note" role="alert">
            {taskActionNote.text}
          </div>
        )}
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn't load tasks. Check your connection and try again.</div>
        )}
        {state.status === 'ready' && (
          <>
            <div className="sec-h">Missing a duration</div>
            <div className="list">
              {state.unprocessed.length === 0 ? (
                <div className="empty">Nothing to process.</div>
              ) : (
                state.unprocessed.map((t) => (
                  <div className="row" key={t.id}>
                    <div className="body">
                      <div className="title">{t.title}</div>
                      <div className="meta chipset">
                        {UNPROCESSED_BUCKETS.map((b) => (
                          <button
                            key={b}
                            className="pill dur"
                            aria-label={`${durLabel(b)} — ${t.title}`}
                            disabled={busyIds.has(t.id)}
                            onClick={() => handleDuration(t.id, b, t.title)}
                          >
                            {durLabel(b)}
                          </button>
                        ))}
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
            <div className="sec-h">Stale — reword, slice smaller, or delete</div>
            <div className="list">
              {state.stale.length === 0 ? (
                // Phil settled this in-session on 2026-09-20: its own literal, not the shared one.
                // The inventory's rule ("the heading is the nudge") still holds — the empty pile
                // stays visible under its heading — but "Nothing to process." misdescribes a pile
                // whose responses are reword, slice smaller, or delete. Stale items are never
                // processed.
                <div className="empty">Nothing stale.</div>
              ) : (
                state.stale.map((t) => (
                  <div className="row" key={t.id}>
                    <div className="body">
                      <div className="title">{t.title}</div>
                      <div className="meta">
                        {t.duration !== null && <span className="pill dur">{durLabel(t.duration)}</span>}
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
            <div className="note">
              These two piles only ever nudge through the reminder footer. Neither gets a
              notification of its own.
            </div>
          </>
        )}
      </div>
    </>
  )
}
