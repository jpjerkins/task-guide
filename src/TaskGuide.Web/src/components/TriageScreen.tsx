import { useCallback, useEffect, useState } from 'react'
import { fetchTasks, setTaskDuration, type Task } from '../api/client'
import { ScreenNav } from './shared/ScreenNav'

const UNPROCESSED_BUCKETS = ['2', '10', '30', '60', 'longer']

// Mirrors DurationCeiling.UnsizedBucketOf, same rule as TasksScreen.tsx's durLabel (copied, not
// imported — the two lanes don't share components).
function durLabel(bucket: string) {
  return Number.isNaN(Number(bucket)) ? 'Longer' : `${bucket}m`
}

export function TriageScreen() {
  const [unprocessed, setUnprocessed] = useState<Task[]>([])
  const [stale, setStale] = useState<Task[]>([])
  const [busyId, setBusyId] = useState<string | null>(null)

  const load = useCallback(async () => {
    const [un, st] = await Promise.all([fetchTasks('unprocessed'), fetchTasks('stale')])
    setUnprocessed(un)
    setStale(st)
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function handleDuration(taskId: string, bucket: string) {
    setBusyId(taskId)
    try {
      await setTaskDuration(taskId, bucket)
    } finally {
      await load()
      setBusyId(null)
    }
  }

  return (
    <div>
      <ScreenNav title="Process" sub={`${unprocessed.length} unprocessed · ${stale.length} stale`} />
      <div className="scroll">
        <div className="sec-h">Missing a duration</div>
        <div className="list">
          {unprocessed.length === 0 ? (
            <div className="empty">Nothing to process.</div>
          ) : (
            unprocessed.map((t) => (
              <div className="row" key={t.id}>
                <div className="body">
                  <div className="title">{t.title}</div>
                  <div className="meta chipset">
                    {UNPROCESSED_BUCKETS.map((b) => (
                      <button
                        key={b}
                        className="pill dur"
                        disabled={busyId === t.id}
                        onClick={() => handleDuration(t.id, b)}
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
          {stale.length === 0 ? (
            // The inventory names exactly one empty-pile string ("Nothing to process.") and is
            // silent on whether an empty stale pile gets a different one — this is a reading of
            // the spec, reusing the same literal, not a copy-paste.
            <div className="empty">Nothing to process.</div>
          ) : (
            stale.map((t) => (
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
          These two piles only ever nudge through the reminder footer. Neither gets a notification
          of its own.
        </div>
      </div>
    </div>
  )
}
