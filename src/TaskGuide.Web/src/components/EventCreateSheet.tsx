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
    return id !== null && resolutions[id] !== undefined
  })
  const canSubmit = name.trim().length > 0 && validTimeRange && allOverlapsResolved && !submitting

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
          <label className="lbl" htmlFor="event-name">Name</label>
          <input id="event-name" className="field" value={name} onChange={(event) => setName(event.target.value)} />
          <label className="lbl" htmlFor="event-start">Start</label>
          <input id="event-start" className="field" value={start} onChange={(event) => setStart(event.target.value)} />
          <label className="lbl" htmlFor="event-end">End</label>
          <input id="event-end" className="field" value={end} onChange={(event) => setEnd(event.target.value)} />
          {!validTimeRange && <div className="hint">End time must be after the start time.</div>}
        </div>
        {overlaps.length > 0 ? (
          <div className="stack">
            <div className="hint">
              This clashes with {overlaps.map((window) => `${window.name}, ${window.start}–${window.end}`).join('; ')}. A date has one shape, so each window has to give way somehow.
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
                      {optionLabel(option, window, start, end)}
                    </button>
                  ))}
                </div>
              )
            })}
            <div className="hint">Only options that preserve a window are offered. Whichever you pick, this date becomes a one-off day.</div>
          </div>
        ) : (
          <div className="stack">
            <div className="hint">No window overlaps, so there is nothing to resolve and the date keeps following its shape.</div>
          </div>
        )}
        <div className="stack">
          <button className="btn primary wide" disabled={!canSubmit} onClick={submit}>Add event</button>
        </div>
      </div>
    </div>
  )
}
