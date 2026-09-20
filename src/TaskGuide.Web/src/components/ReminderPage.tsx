import { useCallback, useEffect, useState } from 'react'
import { ApiError, getJson, sendJson } from '../api/client'
import type { components } from '../api/schema'
import { DateEntry } from './shared/DateEntry'
import { ScreenNav } from './shared/ScreenNav'

type ReminderPageResponse = components['schemas']['ReminderPageResponse']
type ReminderTaskResponse = components['schemas']['ReminderTaskResponse']
type TaskResponse = components['schemas']['TaskResponse']
type DimensionResponse = components['schemas']['DimensionResponse']

type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; page: ReminderPageResponse }

const UNPROCESSED_BUCKETS = ['2', '10', '30', '60', 'longer']

// Display format: "3p", "7:30a", "12p". Storage/wire stays 24h. Ported from
// docs/prototypes/ui-screens.prototype.html's hm() — settled decision, no client clock involved
// (this only formats server-given strings).
function hm(time: string): string {
  const h = Number(time.slice(0, 2))
  const m = Number(time.slice(3, 5))
  const h12 = h % 12 === 0 ? 12 : h % 12
  return (m ? `${h12}:${String(m).padStart(2, '0')}` : `${h12}`) + (h < 12 ? 'a' : 'p')
}

// Mirrors DurationCeiling.UnsizedBucketOf, same rule as TasksScreen.tsx's durLabel (copied, not
// imported — the two lanes don't share components).
function durLabel(bucket: string) {
  return Number.isNaN(Number(bucket)) ? 'Longer' : `${bucket}m`
}

function toggledDeclared(
  dim: DimensionResponse,
  value: string,
  declared: Record<string, string[]>,
): Record<string, string[]> {
  const next = { ...declared }
  if (dim.algebra === 'ordinal') {
    next[dim.id] = [value]
    return next
  }
  const current = next[dim.id] ?? []
  const updated = current.includes(value) ? current.filter((v) => v !== value) : [...current, value]
  if (updated.length === 0) {
    delete next[dim.id]
  } else {
    next[dim.id] = updated
  }
  return next
}

function footerLine(footer: ReminderPageResponse['footer']): string | null {
  const toProcess = Number(footer.toProcess)
  const stale = Number(footer.stale)
  const orphans = Number(footer.orphans)
  const parts: string[] = []
  if (toProcess) parts.push(`${toProcess} to process`)
  if (stale) parts.push(`${stale} stale`)
  if (orphans) parts.push(`${orphans} orphans`)
  return parts.length ? parts.join(' · ') : null
}

function dimensionLabel(id: string, dimensions: DimensionResponse[]): string {
  const dim = dimensions.find((d) => d.id === id)
  return dim?.label ?? id.charAt(0).toUpperCase() + id.slice(1)
}

export function ReminderPage({ date, windowId }: { date: string; windowId: string }) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [dimensions, setDimensions] = useState<DimensionResponse[]>([])
  const [unprocessedTask, setUnprocessedTask] = useState<TaskResponse | null>(null)
  const [snoozeNote, setSnoozeNote] = useState<string | null>(null)
  const [matchingOnNote, setMatchingOnNote] = useState<string | null>(null)
  const [justAdjusted, setJustAdjusted] = useState(false)
  const [postponeOpenId, setPostponeOpenId] = useState<string | null>(null)
  const [postponeDate, setPostponeDate] = useState<string | null>(null)
  const [matchingOnBusy, setMatchingOnBusy] = useState(false)
  const [completingId, setCompletingId] = useState<string | null>(null)
  const [durationBusyId, setDurationBusyId] = useState<string | null>(null)
  const [taskActionNote, setTaskActionNote] = useState<string | null>(null)

  const path = `/api/reminders/${date}/${windowId}`

  // Re-reads must not unmount the page: once 'ready', a failed re-read leaves the last known
  // page in place rather than reverting to the loading/error placeholder.
  const reload = useCallback(async (): Promise<ReminderPageResponse | null> => {
    try {
      const fresh = await getJson<ReminderPageResponse>(path)
      if (fresh === null) throw new Error(`GET ${path} returned no page`)
      setState({ status: 'ready', page: fresh })
      return fresh
    } catch {
      setState((prev) => (prev.status === 'ready' ? prev : { status: 'error' }))
      return null
    }
  }, [path])

  const loadUnprocessedTask = useCallback(async () => {
    try {
      const tasks = await getJson<TaskResponse[]>('/api/tasks?status=unprocessed')
      setUnprocessedTask(tasks?.[0] ?? null)
    } catch {
      setUnprocessedTask(null)
    }
  }, [])

  useEffect(() => {
    setState({ status: 'loading' })
    reload()
  }, [reload])

  useEffect(() => {
    getJson<DimensionResponse[]>('/api/dimensions')
      .then((d) => setDimensions(d ?? []))
      .catch(() => setDimensions([]))
  }, [])

  const hasUnprocessed = state.status === 'ready' && Number(state.page.footer.toProcess) > 0

  useEffect(() => {
    if (!hasUnprocessed) {
      setUnprocessedTask(null)
      return
    }
    loadUnprocessedTask()
    // Gated on whether the pile is non-empty, not the count: nothing but this page's own
    // duration write ever changes the count, and that write owns its own refresh (see
    // finishDuration) — this effect only needs to fire on the loading→ready transition and
    // when the pile empties or refills.
  }, [hasUnprocessed, loadUnprocessedTask])

  // Every branch below is reachable from a cold notification link, so every branch carries the
  // exit — the error branch most of all, since a deleted or rescheduled Window 404s here. A cold
  // link has no origin screen to name, hence the generic "Done" rather than a destination.
  const exit = { label: 'Done', onBack: () => window.location.assign('/') }

  if (state.status === 'loading') {
    return (
      <div>
        <ScreenNav title="" back={exit} />
        <div className="scroll">
          <div className="empty">Loading…</div>
        </div>
      </div>
    )
  }
  if (state.status === 'error') {
    return (
      <div>
        <ScreenNav title="" back={exit} />
        <div className="scroll">
          <div className="empty">Couldn't load this reminder.</div>
        </div>
      </div>
    )
  }

  const page = state.page
  const title = page.windowName ?? page.fallbackEventName ?? ''
  const sub =
    page.windowStart && page.windowEnd
      ? `${hm(page.windowStart)}–${hm(page.windowEnd)} · ${page.date}`
      : page.date

  async function handleComplete(taskId: string) {
    setTaskActionNote(null)
    setCompletingId(taskId)
    try {
      await sendJson('POST', `/api/tasks/${taskId}/completions`, undefined)
    } catch {
      await reload()
      setTaskActionNote("Couldn't mark this off.")
      setCompletingId(null)
      return
    }
    await reload()
    setCompletingId(null)
  }

  async function handlePostpone(taskId: string, next: string) {
    setTaskActionNote(null)
    try {
      await sendJson('PUT', `/api/tasks/${taskId}/postpone`, { date: next })
    } catch {
      setPostponeOpenId(null)
      setPostponeDate(null)
      await reload()
      setTaskActionNote("Couldn't postpone this task.")
      return
    }
    setPostponeOpenId(null)
    setPostponeDate(null)
    await reload()
  }

  async function handleDuration(taskId: string, duration: string) {
    setTaskActionNote(null)
    setDurationBusyId(taskId)
    const finishDuration = async () => {
      const fresh = await reload()
      // The write owns its own refresh: one list read, and it lands before the buttons
      // re-enable. A `fresh` page saying the pile is empty needs no read at all; a failed
      // re-read (null) still re-reads, so a repaired Task never lingers with live buttons.
      if (fresh && Number(fresh.footer.toProcess) === 0) {
        setUnprocessedTask(null)
      } else {
        await loadUnprocessedTask()
      }
      setDurationBusyId(null)
    }
    try {
      await sendJson('PUT', `/api/tasks/${taskId}/duration`, { duration })
    } catch (err) {
      await finishDuration()
      const reason = err instanceof ApiError ? err.reason : null
      setTaskActionNote(reason ?? "Couldn't set this task's duration.")
      return
    }
    await finishDuration()
  }

  async function handleSnooze(interval: number) {
    setSnoozeNote(null)
    try {
      await sendJson('POST', `/api/reminders/${date}/${windowId}/snooze`, undefined)
      setSnoozeNote(`Snoozed — back in ${interval} min.`)
      await reload()
    } catch {
      const fresh = await reload()
      // A refusal shows up as the server's own suppression line (rendered below, from `fresh`).
      // Anything else — including the re-read itself failing offline — is a plain failed write
      // with nothing else to explain it.
      if (!fresh || (fresh.snooze && !fresh.snooze.suppression)) {
        setSnoozeNote("Couldn't snooze — try again.")
      }
    }
  }

  async function handleToggleChip(dim: DimensionResponse, value: string) {
    setMatchingOnNote(null)
    setMatchingOnBusy(true)
    const dimensions = toggledDeclared(dim, value, page.matchingOn.declared)
    try {
      await sendJson('PUT', '/api/right-now/matching-on', { date, windowId, dimensions })
      setJustAdjusted(true)
      await reload()
    } catch {
      const fresh = await reload()
      if (!fresh || fresh.isLive) {
        setMatchingOnNote("Couldn't change what this matches on — try again.")
      }
    } finally {
      setMatchingOnBusy(false)
    }
  }

  const matchingOnAxes = Array.from(
    new Set([...Object.keys(page.matchingOn.declared), ...Object.keys(page.matchingOn.defaulted)]),
  )
  const footerText = footerLine(page.footer)
  const snooze = page.snooze

  return (
    <div>
      <ScreenNav title={title} sub={sub} back={exit} />
      <div className="scroll">
        {page.isLive ? (
          <div className="adjust">
            <div className="adjust-h">
              <span className="lbl">Matching on</span>
              <span className="adjust-sum">
                {matchingOnAxes.map((axis) => {
                  const declaredValues = page.matchingOn.declared[axis]
                  const values = declaredValues ?? page.matchingOn.defaulted[axis] ?? []
                  return (
                    <span key={axis} className={`pill ${declaredValues ? 'now' : 'dim'}`}>
                      {axis}: {values.join(' / ')}
                    </span>
                  )
                })}
              </span>
            </div>
            <div className="adjust-body">
              {dimensions
                .filter((d) => d.source === 'authored')
                .map((dim) => (
                  <div className="chipset" key={dim.id}>
                    {dim.values.map((v) => {
                      const effective = page.matchingOn.declared[dim.id] ?? page.matchingOn.defaulted[dim.id] ?? []
                      return (
                        <button
                          key={`${dim.id}-${v}`}
                          type="button"
                          aria-pressed={effective.includes(v)}
                          disabled={matchingOnBusy}
                          onClick={() => handleToggleChip(dim, v)}
                        >
                          {v}
                        </button>
                      )
                    })}
                  </div>
                ))}
            </div>
            {matchingOnNote && <div className="note">{matchingOnNote}</div>}
            {justAdjusted && (
              <div className="adjust-bar">
                <span>{date} is now an override</span>
              </div>
            )}
          </div>
        ) : (
          page.staleLine && <div className="note">{page.staleLine}</div>
        )}
        {taskActionNote && <div className="note">{taskActionNote}</div>}
        <div className="list">
          {page.matches.length === 0 ? (
            <div className="empty">
              Nothing fits.
              {page.firedAs === null && (
                <>
                  <br />
                  No notification would have fired.
                </>
              )}
            </div>
          ) : (
            page.matches.map((t: ReminderTaskResponse) => (
              <div className="row" key={t.id}>
                <button
                  className="tick"
                  aria-label={`Mark ${t.title} done`}
                  disabled={completingId === t.id}
                  onClick={() => handleComplete(t.id)}
                >
                  ✓
                </button>
                <div className="body">
                  <div className="title">{t.title}</div>
                  <div className="meta">{t.duration !== null && <span className="pill dur">{durLabel(t.duration)}</span>}</div>
                  <button
                    type="button"
                    onClick={() => {
                      setPostponeDate(null)
                      setPostponeOpenId(postponeOpenId === t.id ? null : t.id)
                    }}
                  >
                    Not now
                  </button>
                  {postponeOpenId === t.id && (
                    <>
                      <DateEntry key={`postpone-${t.id}`} label="Not now" value={postponeDate} onChange={setPostponeDate} />
                      <button type="button" disabled={!postponeDate} onClick={() => postponeDate && handlePostpone(t.id, postponeDate)}>
                        Postpone
                      </button>
                    </>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
        {snooze?.suppression && <div className="note">{snooze.suppression}</div>}
        <div className="btn-row">
          {snooze && !snooze.suppression && (
            <button className="btn" onClick={() => handleSnooze(Number(snooze.intervalMinutes))}>
              Snooze {Number(snooze.intervalMinutes)} min
            </button>
          )}
          {/* The page's other exit, alongside the nav's "Done" back button — unconditional because
              a cold notification link has no origin screen, so it can't disappear with Snooze. */}
          <button className="btn ghost" onClick={exit.onBack}>
            Done for now
          </button>
        </div>
        {snoozeNote && <div className="note">{snoozeNote}</div>}
        {(footerText || page.failedFetches.length > 0) && (
          <div className="footer-count">
            {footerText && <span className="pill">{footerText}</span>}
            {page.failedFetches.map((id) => (
              <span key={id} className="pill due">
                {dimensionLabel(id, dimensions)} unavailable
              </span>
            ))}
          </div>
        )}
        {unprocessedTask && (
          <div className="row">
            <div className="body">
              <div className="title">{unprocessedTask.title}</div>
              <div className="meta chipset">
                {UNPROCESSED_BUCKETS.map((b) => (
                  <button
                    key={b}
                    className="pill dur"
                    aria-label={`${durLabel(b)} — ${unprocessedTask.title}`}
                    disabled={durationBusyId === unprocessedTask.id}
                    onClick={() => handleDuration(unprocessedTask.id, b)}
                  >
                    {durLabel(b)}
                  </button>
                ))}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
