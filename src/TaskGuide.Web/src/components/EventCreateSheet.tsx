import { useState } from 'react'
import { sendJson } from '../api/client'
import type { components } from '../api/schema'

export type EventWindow = components['schemas']['AvailabilityWindow']

interface EventCreateSheetProps {
  date: string
  windows: EventWindow[]
  onCancel: () => void
  onCreated: () => void | Promise<void>
}

type Resolution = 'replace' | 'truncateStart' | 'truncateEnd' | 'split'

function minutes(time: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(time)
  if (match === null) return null

  const hours = Number(match[1])
  const mins = Number(match[2])
  return hours <= 23 && mins <= 59 ? hours * 60 + mins : null
}

function overlapResolution(window: EventWindow, start: string, end: string): Resolution | null {
  const windowStart = minutes(window.start)
  const windowEnd = minutes(window.end)
  const eventStart = minutes(start)
  const eventEnd = minutes(end)
  if (windowStart === null || windowEnd === null || eventStart === null || eventEnd === null
    || windowStart >= eventEnd || eventStart >= windowEnd) {
    return null
  }
  return 'replace'
}

function windowId(window: EventWindow): string | null {
  return window.id.value ?? null
}

function optionsFor(window: EventWindow, start: string, end: string): Resolution[] {
  const windowStart = minutes(window.start)
  const windowEnd = minutes(window.end)
  const eventStart = minutes(start)
  const eventEnd = minutes(end)
  if (windowStart === null || windowEnd === null || eventStart === null || eventEnd === null) return []

  const options: Resolution[] = ['replace']
  if (eventStart > windowStart) options.push('truncateEnd')
  if (eventStart > windowStart && eventEnd < windowEnd) options.push('split')
  else if (eventEnd < windowEnd) options.push('truncateStart')
  return options
}

function optionLabel(option: Resolution, window: EventWindow, start: string, end: string): string {
  switch (option) {
    case 'replace':
      return `Replace the window — ${window.name}`
    case 'truncateEnd':
      return `Truncate it to ${window.start}–${start} — ${window.name}`
    case 'split':
      return `Split it around the event — ${window.start}–${start} and ${end}–${window.end} — ${window.name}`
    case 'truncateStart':
      return `Push it to after the event — ${end}–${window.end} — ${window.name}`
  }
}

function optionDescription(option: Resolution, window: EventWindow, start: string, end: string): string {
  const windowStart = minutes(window.start)
  const windowEnd = minutes(window.end)
  const eventStart = minutes(start)
  const eventEnd = minutes(end)
  if (windowStart === null || windowEnd === null || eventStart === null || eventEnd === null) return ''

  switch (option) {
    case 'replace':
      return `${window.name} disappears that day`
    case 'truncateEnd':
      return `Fires at ${window.start} as before; ${eventStart - windowStart} min instead of ${windowEnd - windowStart}`
    case 'split':
      return `${window.start}–${start} and ${end}–${window.end} — two windows, two fires`
    case 'truncateStart':
      return `${end}–${window.end}; the event covers its whole start`
  }
}

function formatDate(date: string): string {
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
    .format(new Date(`${date}T00:00:00Z`))
}

export function EventCreateSheet({ date, windows, onCancel, onCreated }: EventCreateSheetProps) {
  const [name, setName] = useState('')
  const [start, setStart] = useState('06:00')
  const [end, setEnd] = useState('07:00')
  const [resolutions, setResolutions] = useState<Record<string, Resolution>>({})
  const [submitting, setSubmitting] = useState(false)

  const startMinutes = minutes(start)
  const endMinutes = minutes(end)
  const validTimeRange = startMinutes !== null && endMinutes !== null && startMinutes < endMinutes
  const overlaps = validTimeRange
    ? windows.filter((window) => overlapResolution(window, start, end) !== null)
    : []
  const allOverlapsResolved = overlaps.every((window) => {
    const id = windowId(window)
    const resolution = id === null ? undefined : resolutions[id]
    return resolution !== undefined && optionsFor(window, start, end).includes(resolution)
  })
  const canSubmit = name.trim().length > 0 && validTimeRange && allOverlapsResolved && !submitting
  const firstOverlap = overlaps[0]

  async function submit() {
    if (!canSubmit) return

    setSubmitting(true)
    try {
      const resolved = overlaps.flatMap((window) => {
        const id = windowId(window)
        const resolution = id === null ? undefined : resolutions[id]
        return id === null || resolution === undefined ? [] : [{ windowId: id, resolution }]
      })
      await sendJson('POST', '/api/events', {
        date,
        name: name.trim(),
        start,
        end,
        tags: null,
        absenceNotice: null,
        resolutions: resolved.length === 0 ? null : resolved,
      })
      await onCreated()
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="veil" onClick={onCancel}>
      <div className="sheet" onClick={(event) => event.stopPropagation()}>
        <div className="grabber" />
        <div className="sheet-h">
          <h2>New event</h2>
          <button className="icon" onClick={onCancel}>Cancel</button>
        </div>
        <div className="stack">
          <div className="lbl">Name</div>
          <input aria-label="Name" className="field" value={name} onChange={(event) => setName(event.target.value)} />
          <div className="lbl">When — {formatDate(date)}</div>
          <div>
            <input aria-label="Start" className="field time" value={start} onChange={(event) => setStart(event.target.value)} />
            <span>to</span>
            <input aria-label="End" className="field time" value={end} onChange={(event) => setEnd(event.target.value)} />
          </div>
          {!validTimeRange && <div className="hint">End time must be after the start time.</div>}
        </div>
        {firstOverlap !== undefined && (
          <>
            <div className="scope shared">
              <span className="g">⚠</span>
              <span>
                This clashes with <b>{firstOverlap.name}</b>, {firstOverlap.start}–{firstOverlap.end}. A date has <b>one shape</b>, so the window has to give way somehow.
                {overlaps.length > 1 && <> It also overlaps <b>{overlaps.slice(1).map((window) => window.name).join(', ')}</b>.</>}
              </span>
            </div>
            {overlaps.map((window) => {
              const id = windowId(window)
              return (
                <div className="stack" key={id ?? window.name}>
                  {optionsFor(window, start, end).map((option) => (
                    <button
                      className="btn wide"
                      key={option}
                      onClick={() => id !== null && setResolutions((current) => ({ ...current, [id]: option }))}
                      aria-pressed={id !== null && resolutions[id] === option}
                    >
                      {optionLabel(option, window, start, end)}<br />
                      <span>{optionDescription(option, window, start, end)}</span>
                    </button>
                  ))}
                </div>
              )
            })}
            <div className="note">Only the options that actually produce something are offered. Whichever you pick, {formatDate(date)} becomes a <b>one-off day</b>.</div>
          </>
        )}
        <div className="btn-row">
          <button className="btn primary wide" disabled={!canSubmit} onClick={submit}>Add event</button>
        </div>
        {firstOverlap === undefined && (
          <div className="note">No window overlaps, so there is nothing to resolve and the date keeps following its shape. Most events land here — the three-way prompt is the exception, not the rule.</div>
        )}
      </div>
    </div>
  )
}
