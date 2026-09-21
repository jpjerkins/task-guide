import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { fetchDimensions, fetchTask, type DimensionResponse, type Task } from '../api/client'
import { ScreenNav } from './shared/ScreenNav'

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
    task.opportunities === null ? null : task.zeroKind === 'unknown' ? (
      <span className="which">Opportunities: unknown — a fetched Dimension check failed.</span>
    ) : task.opportunities === 0 && task.zeroKind === 'noneInThisStretch' ? (
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

export function TaskDetail({ taskId, now = new Date() }: { taskId: string; now?: Date }) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
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
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn't load this task. Check your connection and try again.</div>
        )}
        {state.status === 'ready' && (
          <>
            {fitBar(state.task, state.dimensions, today)}
            <div className="stack" />
          </>
        )}
      </div>
    </>
  )
}
