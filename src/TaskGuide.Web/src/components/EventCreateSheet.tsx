import { useState } from 'react'
import { sendJson } from '../api/client'
import type { components } from '../api/schema'

export type EventWindow = components['schemas']['AvailabilityWindow']
type CreateEventRequest = components['schemas']['CreateEventRequest']
type EventOverlapResolutionRequest = components['schemas']['EventOverlapResolutionRequest']

interface EventCreateSheetProps {
  date: string
  windows: EventWindow[]
  onCancel: () => void
  onCreated: () => void | Promise<void>
}

const resolutionValues = ['replace', 'truncateStart', 'truncateEnd', 'split'] as const satisfies readonly EventOverlapResolutionRequest['resolution'][]
type Resolution = (typeof resolutionValues)[number]

interface EventTimes {
  start: string
  end: string
  startMinutes: number
  endMinutes: number
}

interface Overlap extends EventTimes {
  window: EventWindow
  windowStart: string
  windowEnd: string
  windowStartMinutes: number
  windowEndMinutes: number
}

function parseTime(value: string): string | null {
  const time = value.trim().toLowerCase().replace(/[\s.]/g, (character) => character === '.' ? ':' : '')
  const match = /^(\d{1,2}):?(\d{2})?\s*(am|pm|a|p)?$/.exec(time)
  if (match === null) return null

  let hours = Number(match[1])
  const mins = match[2] === undefined ? 0 : Number(match[2])
  const amPm = match[3]?.[0]
  if (mins > 59) return null
  if (amPm !== undefined) {
    if (hours < 1 || hours > 12) return null
    hours = (hours % 12) + (amPm === 'p' ? 12 : 0)
  } else if (hours > 23) return null
  return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}`
}

function toMinutes(time: string): number {
  return Number(time.slice(0, 2)) * 60 + Number(time.slice(3))
}

function eventTimes(start: string, end: string): EventTimes | null {
  const normalizedStart = parseTime(start)
  const normalizedEnd = parseTime(end)
  if (normalizedStart === null || normalizedEnd === null) return null
  const startMinutes = toMinutes(normalizedStart)
  const endMinutes = toMinutes(normalizedEnd)
  return startMinutes < endMinutes ? { start: normalizedStart, end: normalizedEnd, startMinutes, endMinutes } : null
}

function overlapFor(window: EventWindow, event: EventTimes): Overlap | null {
  const windowStart = parseTime(window.start)
  const windowEnd = parseTime(window.end)
  if (windowStart === null || windowEnd === null) return null
  const windowStartMinutes = toMinutes(windowStart)
  const windowEndMinutes = toMinutes(windowEnd)
  if (windowStartMinutes >= event.endMinutes || event.startMinutes >= windowEndMinutes) {
    return null
  }
  return { ...event, window, windowStart, windowEnd, windowStartMinutes, windowEndMinutes }
}

function windowId(window: EventWindow): string | null {
  return window.id.value ?? null
}

function optionsFor(overlap: Overlap): Resolution[] {
  const options: Resolution[] = ['replace']
  if (overlap.startMinutes > overlap.windowStartMinutes) options.push('truncateEnd')
  if (overlap.startMinutes > overlap.windowStartMinutes && overlap.endMinutes < overlap.windowEndMinutes) options.push('split')
  else if (overlap.endMinutes < overlap.windowEndMinutes) options.push('truncateStart')
  return options
}

function optionCopy(option: Resolution, overlap: Overlap): { label: string; description: string } {
  const { window, start, end, windowStart, windowEnd, startMinutes, windowStartMinutes, windowEndMinutes } = overlap
  switch (option) {
    case 'replace':
      return { label: `Replace the window — ${window.name}`, description: `${window.name} disappears that day` }
    case 'truncateEnd':
      return { label: `Truncate it to ${windowStart}–${start} — ${window.name}`, description: `Fires at ${windowStart} as before; ${startMinutes - windowStartMinutes} min instead of ${windowEndMinutes - windowStartMinutes}` }
    case 'split':
      return { label: `Split it around the event — ${windowStart}–${start} and ${end}–${windowEnd} — ${window.name}`, description: `${windowStart}–${start} and ${end}–${windowEnd} — two windows, two fires` }
    case 'truncateStart':
      return { label: `Push it to after the event — ${end}–${windowEnd} — ${window.name}`, description: `${end}–${windowEnd}; the event covers its whole start` }
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

  const event = eventTimes(start, end)
  const overlaps = event === null
    ? []
    : windows.flatMap((window) => {
      const overlap = overlapFor(window, event)
      return overlap === null ? [] : [overlap]
    })
  const allOverlapsResolved = overlaps.every((overlap) => {
    const id = windowId(overlap.window)
    const resolution = id === null ? undefined : resolutions[id]
    return resolution !== undefined && optionsFor(overlap).includes(resolution)
  })
  const canSubmit = name.trim().length > 0 && event !== null && allOverlapsResolved && !submitting
  const firstOverlap = overlaps[0]

  async function submit() {
    if (!canSubmit) return

    setSubmitting(true)
    try {
      const resolved: EventOverlapResolutionRequest[] = overlaps.flatMap((overlap) => {
        const id = windowId(overlap.window)
        const resolution = id === null ? undefined : resolutions[id]
        return id === null || resolution === undefined ? [] : [{ windowId: id, resolution }]
      })
      const request: CreateEventRequest = {
        date,
        name: name.trim(),
        start: event.start,
        end: event.end,
        tags: null,
        absenceNotice: null,
        resolutions: resolved.length === 0 ? null : resolved,
      }
      await sendJson('POST', '/api/events', request)
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
          {event === null && <div className="hint">End time must be after the start time.</div>}
        </div>
        {firstOverlap !== undefined && (
          <>
            <div className="scope shared">
              <span className="g">⚠</span>
              <span>
                This clashes with <b>{firstOverlap.window.name}</b>, {firstOverlap.windowStart}–{firstOverlap.windowEnd}. A date has <b>one shape</b>, so the window has to give way somehow.
                {overlaps.length > 1 && <> It also overlaps <b>{overlaps.slice(1).map((overlap) => overlap.window.name).join(', ')}</b>.</>}
              </span>
            </div>
            {overlaps.map((overlap) => {
              const id = windowId(overlap.window)
              return (
                <div className="stack" key={id ?? overlap.window.name}>
                  {optionsFor(overlap).map((option) => {
                    const copy = optionCopy(option, overlap)
                    return (
                    <button
                      className="btn wide"
                      key={option}
                      onClick={() => id !== null && setResolutions((current) => ({ ...current, [id]: option }))}
                      aria-pressed={id !== null && resolutions[id] === option}
                    >
                      {copy.label}<br />
                      <span>{copy.description}</span>
                    </button>
                    )
                  })}
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
