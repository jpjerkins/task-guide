import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ApiError,
  clearPostpone,
  deferTaskByOffset,
  fetchDimensions,
  fetchTask,
  saveTaskDetails,
  type DimensionResponse,
  type Task,
} from '../api/client'
import { DateEntry } from './shared/DateEntry'
import { ScreenNav } from './shared/ScreenNav'

// Duration's five buckets (KnownDimensions.DurationBuckets), same rule TasksScreen.tsx's durLabel
// and TriageScreen.tsx's durLabel use — copied, not imported, since the two Web lanes deliberately
// don't share components.
const DURATION_BUCKETS = ['2', '10', '30', '60', 'longer']
function durLabel(bucket: string) {
  return Number.isNaN(Number(bucket)) ? 'Longer' : `${bucket}m`
}

// TaskEndpoints.ToResponse builds `dimensions` from Task.Tags.Dimensions, and Duration IS a
// declared Dimension (KnownDimensions.Default) — for the window side of matching — so a Task's
// raw wire `dimensions` can carry a `duration` key. The Duration *field* on this form is authored
// separately (the chipset), and UpdateTaskDetails.Invalid refuses a write whose `dimensions` still
// carries that key ("duration belongs in the duration field"). Stripped here, at the one place the
// write payload is built, rather than trusting `pickableDimensions` (which only keeps it off the
// render) to keep it out of every path that touches `dims`.
function withoutDurationKey(dims: Record<string, string[]>): Record<string, string[]> {
  const { duration: _duration, ...rest } = dims
  return rest
}

const OFFSET_UNITS = [
  { value: 0, label: 'Days' },
  { value: 1, label: 'Weeks' },
  { value: 2, label: 'Months' },
]

// Same three-arm shape as TasksScreen.tsx/TriageScreen.tsx: a loading arm so the first paint
// doesn't say "Nothing here." before the first read returns, and an error arm the ready-state
// body can't leak past.
type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; task: Task; dimensions: DimensionResponse[] }

// CONTEXT.md:1795-1801: without a Deadline, Opportunities is a true rolling 7x24h window and
// reads "in the next 7 days"; with a Deadline still ahead it runs to end of that day, reading
// "before it is due". Once the Deadline has passed the bound is dropped and it reverts to the
// rolling 7 days — otherwise the horizon goes negative and every overdue Task misreports as an
// Orphan. This is a labelling comparison against the Chicago-resolved `today`, same precedent as
// TasksScreen.tsx's pastDeadline — never a timing predicate that decides eligibility or status.
function horizonWords(task: Task, today: string): string {
  const deadlineAhead = task.deadline !== null && task.deadline >= today
  return deadlineAhead ? 'before it is due' : 'in the next 7 days'
}

// ADR-0004's amendment (Opportunities.cs:215): a failed Opportunity fetch must not read as 0,
// since 0 is the floor of the Scarcity key. Do not re-run matching in the SPA — `zeroKind` and
// `orphanBlameDimensions` are computed server-side (OrphanDetection.KindOfZero) and rendered
// verbatim.
function fitBar(task: Task, dimensions: DimensionResponse[], today: string) {
  // Unprocessed IS the absence of a Duration, one of matching's two inputs — the count would be
  // computed from a missing operand, so orphan-ness is undefined rather than guessed either way.
  if (task.status === 'unprocessed') {
    return (
      <div className="fitbar">
        <span className="which">Orphan-ness is undefined until this task has a Duration.</span>
      </div>
    )
  }
  // Computable but useless: a Stale Task cannot fire regardless, so no orphan badge renders.
  if (task.status === 'stale') {
    return null
  }

  const countBlock =
    // Checked before the `opportunities === null` case below: OrphanDetection.KindOfZero returns
    // Unknown exactly when opportunities is null on an Active Task, so the real wire payload for
    // "unknown" is `{ opportunities: null, zeroKind: 'unknown' }` — the same opportunities: null
    // shape a deferred/postponed Task carries with a different zeroKind. Checking null first would
    // make the two indistinguishable.
    task.zeroKind === 'unknown' ? (
      <span className="which">Opportunities: unknown — a fetched Dimension check failed.</span>
    ) : task.opportunities === null ? null : task.opportunities === 0 && task.zeroKind === 'noneInThisStretch' ? (
      <span className="which">
        An override or event has taken them all out of this stretch — nothing is wrong with the task.
      </span>
    ) : task.opportunities === 0 && task.zeroKind === 'orphan' ? null : (
      <span className="which">
        <span className="n">{task.opportunities}</span>{' '}
        {task.opportunities === 1 ? 'chance' : 'chances'} {horizonWords(task, today)}
      </span>
    )

  const blamed = task.orphanBlameDimensions.map(
    (id) => dimensions.find((d) => d.id === id)?.label ?? id,
  )
  const orphanBlock =
    task.zeroKind !== 'orphan' ? null : blamed.length > 0 ? (
      <span className="which">No window declares {blamed.join(' or ')} — relax one, or declare it on a window</span>
    ) : (
      <span className="which">No single property is to blame; the combination has no home in this pattern</span>
    )

  return (
    <div className="fitbar">
      {countBlock}
      {orphanBlock}
    </div>
  )
}

// The one form, the one Save (#174: Detail's PUT crosses the lock as one request — Duration
// included — so a later gesture can't race a stale, piecemeal write). Postpone-clear and Defer
// are separate writes with their own re-read, same as TasksScreen.tsx's row gestures, because
// neither is part of the authored-fields PUT.
function TaskForm({
  taskId,
  task,
  dimensions,
  onSaved,
  onNote,
}: {
  taskId: string
  task: Task
  dimensions: DimensionResponse[]
  onSaved: () => Promise<void>
  // Lifted to TaskDetail, rendered outside the three-arm conditional — same reasoning as
  // TasksScreen.tsx's actionNote: the reload a write triggers can itself fail, which unmounts this
  // whole component (the 'error' arm replaces it), and the note must survive that rather than
  // vanishing along with the ready-state body it would otherwise live inside.
  onNote: (text: string | null) => void
}) {
  const [title, setTitle] = useState(task.title)
  const [notes, setNotes] = useState(task.notes)
  const [duration, setDuration] = useState(task.duration)
  const [deadline, setDeadline] = useState(task.deadline)
  const [dims, setDims] = useState(task.dimensions)
  const [offsetValue, setOffsetValue] = useState('')
  const [offsetUnit, setOffsetUnit] = useState(0)
  const [busy, setBusy] = useState(false)

  // Resets the form to match whatever the server just returned — every write this screen makes
  // (Save, clear Postpone, Defer) re-reads afterwards, and the fresh read is the source of truth.
  useEffect(() => {
    setTitle(task.title)
    setNotes(task.notes)
    setDuration(task.duration)
    setDeadline(task.deadline)
    setDims(task.dimensions)
  }, [task])

  function toggleDimensionValue(dimension: DimensionResponse, value: string) {
    setDims((prev) => {
      const current = prev[dimension.id] ?? []
      if (dimension.algebra === 'ordinal') {
        return { ...prev, [dimension.id]: current.includes(value) ? [] : [value] }
      }
      const next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value]
      return { ...prev, [dimension.id]: next }
    })
  }

  async function handleSave() {
    onNote(null)
    setBusy(true)
    try {
      await saveTaskDetails(taskId, {
        title,
        notes,
        duration,
        // TaskEndpoints.DeadlineOf reports a recurring Task's live-instance Deadline as a
        // non-nullable DateOnly, so `deadline` is read as non-null even though it's derived, not
        // authored — UpdateTaskDetails.ExecuteAsync refuses any Save on a recurring Task that
        // carries a non-null Deadline. Sending it back verbatim would 409 every Save.
        deadline: task.recurring ? null : deadline,
        dimensions: withoutDurationKey(dims),
      })
    } catch (err) {
      onNote(err instanceof ApiError && err.reason ? err.reason : "Couldn't save.")
    }
    await onSaved()
    setBusy(false)
  }

  async function handleClearPostpone() {
    onNote(null)
    setBusy(true)
    try {
      await clearPostpone(taskId)
    } catch (err) {
      onNote(err instanceof ApiError && err.reason ? err.reason : "Couldn't clear postpone.")
    }
    await onSaved()
    setBusy(false)
  }

  async function handleDefer() {
    const offset = Number(offsetValue)
    if (!offsetValue || Number.isNaN(offset)) return
    onNote(null)
    setBusy(true)
    try {
      await deferTaskByOffset(taskId, offset, offsetUnit)
    } catch (err) {
      onNote(err instanceof ApiError && err.reason ? err.reason : "Couldn't defer.")
    }
    await onSaved()
    setBusy(false)
  }

  // KnownDimensions.Default declares `duration` as a Dimension too, for the window side of
  // matching — it has its own control here (the chipset above), so it's excluded from this loop.
  const pickableDimensions = dimensions.filter((d) => d.id !== 'duration')

  return (
    <>
      <div className="stack">
        <label className="stack">
          <span className="lbl">Title</span>
          <input className="field" value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="stack">
          <span className="lbl">Notes</span>
          <textarea className="field" value={notes ?? ''} onChange={(e) => setNotes(e.target.value || null)} />
        </label>
        <div className="lbl">Duration</div>
        <div className="chipset">
          {DURATION_BUCKETS.map((b) => (
            <button
              key={b}
              type="button"
              aria-pressed={duration === b}
              disabled={busy}
              onClick={() => setDuration(b)}
            >
              {durLabel(b)}
            </button>
          ))}
        </div>
        {task.recurring ? (
          // Derived (the live instance's), not authored — see the comment on handleSave. A
          // DateEntry here would let the user "edit" a value Save can never actually send.
          <div className="stack">
            <span className="lbl">Deadline</span>
            <div className="field">{task.deadline}</div>
          </div>
        ) : (
          <DateEntry label="Deadline" value={deadline} onChange={setDeadline} disabled={busy} />
        )}
      </div>
      <div className="sec-h">Dimensions</div>
      <div className="stack">
        {pickableDimensions.map((d) => (
          <div key={d.id}>
            <div className="lbl">{d.label}</div>
            <div className="chipset">
              {d.values.map((v) => (
                <button
                  key={v}
                  type="button"
                  aria-pressed={(dims[d.id] ?? []).includes(v)}
                  disabled={busy}
                  onClick={() => toggleDimensionValue(d, v)}
                >
                  {v}
                </button>
              ))}
            </div>
          </div>
        ))}
        {task.looseTags.length > 0 && (
          <>
            <div className="lbl">Unresolved</div>
            <div className="chipset">
              {task.looseTags.map((tag) => (
                <span key={tag} className="pill inert">
                  {tag} · kept but inert
                </span>
              ))}
            </div>
          </>
        )}
      </div>
      {task.postpone !== null && (
        <div className="stack">
          <button type="button" disabled={busy} onClick={handleClearPostpone}>
            Clear postpone
          </button>
        </div>
      )}
      {task.recurring && (
        <div className="stack">
          <div className="lbl">Defer</div>
          <label className="stack">
            <span className="lbl">Offset</span>
            <input
              className="field"
              type="number"
              value={offsetValue}
              disabled={busy}
              onChange={(e) => setOffsetValue(e.target.value)}
            />
          </label>
          <label className="stack">
            <span className="lbl">Unit</span>
            <select
              className="field"
              value={offsetUnit}
              disabled={busy}
              onChange={(e) => setOffsetUnit(Number(e.target.value))}
            >
              {OFFSET_UNITS.map((u) => (
                <option key={u.value} value={u.value}>
                  {u.label}
                </option>
              ))}
            </select>
          </label>
          <button type="button" disabled={busy} onClick={handleDefer}>
            Defer
          </button>
        </div>
      )}
      <div className="btn-row">
        <button type="button" className="btn primary wide" disabled={busy} onClick={handleSave}>
          Save
        </button>
      </div>
      <div className="note">
        Dimensions themselves are declared in code. This screen picks values on them, never edits
        the axes.
      </div>
    </>
  )
}

export function TaskDetail({ taskId, now = new Date() }: { taskId: string; now?: Date }) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [note, setNote] = useState<string | null>(null)
  const loadToken = useRef(0)

  const today = useMemo(
    () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Chicago' }).format(now),
    [now],
  )

  const load = useCallback(async () => {
    const token = ++loadToken.current
    try {
      const [task, dimensions] = await Promise.all([fetchTask(taskId), fetchDimensions()])
      if (loadToken.current !== token) return
      if (task === null) {
        setState({ status: 'error' })
        return
      }
      setState({ status: 'ready', task, dimensions })
    } catch {
      if (loadToken.current !== token) return
      setState({ status: 'error' })
    }
  }, [taskId])

  useEffect(() => {
    load()
  }, [load])

  return (
    <>
      <ScreenNav title="Task" />
      <div className="scroll">
        {note && (
          <div className="note" role="alert">
            {note}
          </div>
        )}
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn't load this task. Check your connection and try again.</div>
        )}
        {state.status === 'ready' && (
          <>
            {fitBar(state.task, state.dimensions, today)}
            <TaskForm
              taskId={taskId}
              task={state.task}
              dimensions={state.dimensions}
              onSaved={load}
              onNote={setNote}
            />
          </>
        )}
      </div>
    </>
  )
}
