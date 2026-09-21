import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, createTask, fetchTasks, sendJson, type Task } from '../api/client'
import { QuickAdd } from './QuickAdd'
import { DateEntry } from './shared/DateEntry'

// Same three-state shape as TriageScreen.tsx: a loading arm so the first paint doesn't say
// "Nothing here." before the first read returns, and an error arm the ready-state counts can't
// leak past.
type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; tasks: Task[] }

// Mirrors DurationCeiling.UnsizedBucketOf: the unsized bucket names no length, so it's
// identified by what it is *not* (parseable as minutes) rather than a literal 'longer'
// match, keeping this in step with the Domain's bucket registry. 'Longer' is the label
// the UI prototypes use (docs/prototypes/ui-screens.prototype.html:437).
function durLabel(bucket: string) {
  return Number.isNaN(Number(bucket)) ? 'Longer' : `${bucket}m`
}

// The four filters, in the prototype's order (ui-screens.prototype.html:1448). Each is matched
// against `Task.status` case-insensitively — Status is read off the wire (StatusRules.Of, via
// TaskEndpoints.ToWireName's camelCase), never derived, ordered or recomputed here.
const FILTERS = ['Active', 'Unprocessed', 'Stale', 'Done'] as const
type Filter = (typeof FILTERS)[number]

// Calendar arithmetic on an already-resolved Chicago date; elapsed hours would drift over DST.
// Copied from OverrideDateSelection.ts rather than imported — the two Web lanes deliberately
// don't share components (see this file's durLabel, copied from TriageScreen.tsx for the same
// reason).
function offsetDate(date: string, days: number): string {
  const shifted = new Date(`${date}T00:00:00Z`)
  shifted.setUTCDate(shifted.getUTCDate() + days)
  return shifted.toISOString().slice(0, 10)
}

function addMonth(date: string): string {
  const shifted = new Date(`${date}T00:00:00Z`)
  shifted.setUTCMonth(shifted.getUTCMonth() + 1)
  return shifted.toISOString().slice(0, 10)
}

interface PostponeInterval {
  label: string
  date: string
}

// task.eligible && !task.recurring && !task.derived — CONTEXT § Postpone / StatusRules.
// CanPostpone: a deferred Task is Status Active but not yet surfaced, and there is no Deferred
// status to gate on instead, so gating on Status would offer the gesture too early.
function canPostpone(task: Task): boolean {
  return task.eligible && !task.recurring && !task.derived
}

function pastDeadline(task: Task, date: string): boolean {
  return task.deadline !== null && date > task.deadline
}

export function TasksScreen({ now = new Date() }: { now?: Date }) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [sheetOpen, setSheetOpen] = useState(false)
  const [filter, setFilter] = useState<Filter>('Active')
  const [postponeOpenId, setPostponeOpenId] = useState<string | null>(null)
  const [pickedDate, setPickedDate] = useState<string | null>(null)
  // A Set, not a single slot: two rows' writes can be in flight at once (TriageScreen.tsx's
  // same reasoning).
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(new Set())
  const [actionNote, setActionNote] = useState<{ taskId: string; text: string } | null>(null)
  // Guards a slow reload from overwriting a newer one — same idea as TriageScreen.tsx's loadToken.
  const loadToken = useRef(0)

  const today = useMemo(
    () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Chicago' }).format(now),
    [now],
  )

  const load = useCallback(async () => {
    const token = ++loadToken.current
    try {
      const tasks = await fetchTasks()
      if (loadToken.current !== token) return
      setState({ status: 'ready', tasks })
    } catch {
      if (loadToken.current !== token) return
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

  async function withBusy(taskId: string, action: () => Promise<void>, failedText: string) {
    setActionNote((prev) => (prev?.taskId === taskId ? null : prev))
    setBusyIds((prev) => new Set(prev).add(taskId))
    try {
      await action()
    } catch (err) {
      const reason = err instanceof ApiError ? err.reason : null
      setActionNote({ taskId, text: reason ? `${failedText} — ${reason}.` : `${failedText}.` })
    }
    await load()
    setBusyIds((prev) => {
      const next = new Set(prev)
      next.delete(taskId)
      return next
    })
  }

  async function handleMarkOff(taskId: string, title: string) {
    await withBusy(
      taskId,
      () => sendJson('POST', `/api/tasks/${taskId}/completions`, undefined),
      `Couldn't mark off "${title}"`,
    )
  }

  async function handlePostpone(taskId: string, title: string, date: string) {
    setPostponeOpenId(null)
    setPickedDate(null)
    await withBusy(
      taskId,
      () => sendJson('PUT', `/api/tasks/${taskId}/postpone`, { date }),
      `Couldn't postpone "${title}"`,
    )
  }

  const tasks = state.status === 'ready' ? state.tasks : []
  const counts: Record<Filter, number> = { Active: 0, Unprocessed: 0, Stale: 0, Done: 0 }
  for (const t of tasks) {
    const label = (t.status.charAt(0).toUpperCase() + t.status.slice(1)) as Filter
    if (label in counts) counts[label] += 1
  }
  const shown = tasks.filter((t) => t.status.toLowerCase() === filter.toLowerCase())

  function intervalsFor(task: Task): PostponeInterval[] {
    return [
      { label: 'Tomorrow', date: offsetDate(today, 1) },
      { label: 'A week', date: offsetDate(today, 7) },
      { label: 'A month', date: addMonth(today) },
    ].map((interval) => ({
      ...interval,
      label: pastDeadline(task, interval.date) ? `${interval.label} · past its deadline` : interval.label,
    }))
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
      <div className="filterbar">
        {FILTERS.map((f) => (
          <button key={f} aria-pressed={filter === f} onClick={() => setFilter(f)}>
            {f} {counts[f]}
          </button>
        ))}
      </div>
      <div className="scroll">
        {/* Rendered outside the three-arm conditional below, same as TriageScreen.tsx's
            taskActionNote: offline, the reload a write triggers can fail too, and the note must
            survive that rather than being dropped along with the ready-state body it would
            otherwise live inside — the error arm replaces that whole body, rows included. The
            Task's title is what ties the note to a row once it's rendered at the top. */}
        {actionNote && (
          <div className="note" role="alert">
            {actionNote.text}
          </div>
        )}
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn't load tasks. Check your connection and try again.</div>
        )}
        {state.status === 'ready' && (
          <div className="list">
            {shown.length === 0 ? (
              <div className="empty">Nothing here.</div>
            ) : (
              shown.map((t) => {
                const postponed = t.postpone !== null
                return (
                  // Greyed the way the prototype greys a Done row (opacity:.45,
                  // ui-screens.prototype.html:775) — inline, not a CSS class: index.css isn't
                  // this lane's.
                  <div className="row" key={t.id} style={postponed ? { opacity: 0.45 } : undefined}>
                    <button
                      className="tick"
                      aria-label={`Mark ${t.title} done`}
                      // ADR-0007: Unprocessed IS the absence of a Duration, so there is nothing
                      // yet to be done within.
                      disabled={t.duration === null || busyIds.has(t.id)}
                      onClick={() => handleMarkOff(t.id, t.title)}
                    >
                      ✓
                    </button>
                    <div className="body">
                      <div className="title">{t.title}</div>
                      <div className="meta">
                        {t.duration !== null ? (
                          <span className="pill dur">{durLabel(t.duration)}</span>
                        ) : (
                          <span className="pill due">no duration</span>
                        )}
                        {/* #174: the window-editor deep-link payload isn't on the wire yet, so
                            the badge renders with no link — a reduced-scope line for this is in
                            tests/TEST-INVENTORY.md. */}
                        {t.status === 'active' && t.zeroKind === 'orphan' && (
                          <span className="pill due">orphan</span>
                        )}
                        {postponed ? (
                          <span className="pill dim">
                            postponed to {t.postpone}
                            {pastDeadline(t, t.postpone as string) ? ' · past its deadline' : ''}
                          </span>
                        ) : (
                          t.defer !== null && <span className="pill dim">surfaces {t.defer}</span>
                        )}
                      </div>
                      {canPostpone(t) && (
                        <>
                          <button
                            type="button"
                            disabled={busyIds.has(t.id)}
                            onClick={() => {
                              setPickedDate(null)
                              setPostponeOpenId((prev) => (prev === t.id ? null : t.id))
                            }}
                          >
                            Not now
                          </button>
                          {postponeOpenId === t.id && (
                            <div className="chipset">
                              {intervalsFor(t).map((interval) => (
                                <button
                                  key={interval.label}
                                  type="button"
                                  onClick={() => handlePostpone(t.id, t.title, interval.date)}
                                >
                                  {interval.label}
                                </button>
                              ))}
                              <DateEntry label="Pick a date…" value={pickedDate} onChange={setPickedDate} />
                              <button
                                type="button"
                                disabled={!pickedDate}
                                onClick={() => pickedDate && handlePostpone(t.id, t.title, pickedDate)}
                              >
                                Postpone
                              </button>
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                )
              })
            )}
          </div>
        )}
      </div>
      {sheetOpen && <QuickAdd onCancel={() => setSheetOpen(false)} onAdd={handleAdd} />}
    </div>
  )
}
