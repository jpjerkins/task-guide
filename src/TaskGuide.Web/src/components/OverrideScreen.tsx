import { useEffect, useState } from 'react'
import { getJson, sendJson } from '../api/client'
import type { components, paths } from '../api/schema'
import { authorOverrideSpan, type OverrideSpan } from './OverrideRange'
import { OverridePromoteSheet } from './OverridePromoteSheet'
import { OverrideRangeSheet } from './OverrideRangeSheet'
import { useOverrideConfirmation } from './OverrideConfirmation'
import { OverrideStampSheet } from './OverrideStampSheet'
import { EventCreateSheet } from './EventCreateSheet'
import { DateRail } from './DateRail'
import { DateEntry } from './shared/DateEntry'
import { ScreenNav } from './shared/ScreenNav'
import { useOverrideDateSelection } from './OverrideDateSelection'
import { fmtShort, hm } from './OverrideFormat'
import { dimPills } from './OverrideDimPills'

type Day = components['schemas']['DayShape']

export function OverrideScreen() {
  const { confirm, presentation } = useOverrideConfirmation()
  const { selectedDate, railSpan, dateEntryProps } = useOverrideDateSelection()
  const [eventOpen, setEventOpen] = useState(false)
  const [promoteOpen, setPromoteOpen] = useState(false)
  const [rangeOpen, setRangeOpen] = useState(false)
  const [stampOpen, setStampOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [labels, setLabels] = useState<Record<string, string>>({})
  const [escapeOpen, setEscapeOpen] = useState(false)
  const [day, setDay] = useState<Day | null>(null)
  const [error, setError] = useState('')
  const [marked, setMarked] = useState<string[]>([])
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    let current = true
    const requests: Promise<Day | null>[] = []
    const date = new Date(`${railSpan.from}T00:00:00Z`)
    while (date.toISOString().slice(0, 10) <= railSpan.to) {
      requests.push(getJson<Day>(`/api/days/${date.toISOString().slice(0, 10)}`))
      date.setUTCDate(date.getUTCDate() + 1)
    }
    Promise.all(requests).then(values => {
      if (current) setMarked(values.flatMap(value => value && (value.isOverridden || value.events.length) ? [value.date] : []))
    }).catch(reason => { if (current) setError(String(reason)) })
    return () => { current = false }
  }, [railSpan, revision])
  useEffect(() => {
    let current = true
    setError('')
    getJson<Day>(`/api/days/${selectedDate}`).then(value => {
      if (current) setDay(value)
    }).catch(reason => { if (current) setError(String(reason)) })
    return () => { current = false }
  }, [selectedDate, revision])
  async function promote(name: string) {
    setBusy(true)
    setError('')
    try {
      await sendJson<components['schemas']['DayTemplateResponse']>('POST', `/api/overrides/${selectedDate}/promote`, { name })
      setPromoteOpen(false)
      setRevision(value => value + 1)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  async function revert() {
    setBusy(true)
    setError('')
    try {
      await sendJson('DELETE', `/api/overrides/${selectedDate}`, undefined)
      setRevision(value => value + 1)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  async function createRange(span: OverrideSpan) {
    setBusy(true)
    setError('')
    try {
      const created = await authorOverrideSpan(span, confirm)
      if (created !== null) {
        setLabels(previous => ({ ...previous, ...Object.fromEntries(created.map(day => [day.date, day.used?.templateName ?? 'One-off day'])) }))
        setRangeOpen(false)
        setRevision(value => value + 1)
      }
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  async function stamp(templateId: string) {
    setBusy(true)
    setError('')
    try {
      const dates = await getJson<paths['/api/overrides/clobber-check']['get']['responses'][200]['content']['application/json']>(`/api/overrides/clobber-check?from=${selectedDate}&to=${selectedDate}`)
      if (dates === null) throw new Error('Clobber check returned no dates response')
      if (dates.length && !await confirm(dates)) return
      const result = await sendJson<components['schemas']['DateOverrideResponse']>('PUT', `/api/overrides/${selectedDate}/stamp`, { templateId })
      if (result?.used) setLabels(previous => ({ ...previous, [selectedDate]: result.used?.templateName ?? 'One-off day' }))
      setStampOpen(false)
      setRevision(value => value + 1)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(false) }
  }
  const modalOpen = stampOpen || rangeOpen || promoteOpen || eventOpen
  const shown = day?.date === selectedDate ? day : null
  // The wire's DayShape carries no template-use name (tests/TEST-INVENTORY.md), so the nav's
  // `sub` and the promote sheet's prefill both fall back to this same degraded label: the name a
  // write in this session supplied, else a generic "override" or "pattern" reading.
  const label = shown && (labels[selectedDate] ?? (shown.isOverridden ? 'One-off day' : 'Following the pattern'))
  return <>
    <ScreenNav title={fmtShort(selectedDate)} sub={label ?? undefined} />
    <DateRail {...railSpan} selected={selectedDate} marked={marked} disabled={busy || modalOpen} onSelect={dateEntryProps.onChange} />
    <div className="btn-row"><button className="btn" disabled={busy || modalOpen} onClick={() => setEscapeOpen(true)}>Pick a date…</button></div>
    {escapeOpen && <div className="stack"><DateEntry {...dateEntryProps} disabled={busy || modalOpen} /></div>}
    <div className="scroll" inert={modalOpen}>
      {error && <div className="note" role="alert">{error}</div>}
      {shown && <>
        <div className="scope one"><span className="g">◈</span><span>Editing <b>{fmtShort(selectedDate)} only</b>. {shown.isOverridden
          ? <>This date is already an override — <b>{labels[selectedDate] ?? 'Override'}</b>.</>
          : <>The first change copies the day off its shape and this date stops following the pattern.</>}</span></div>
        <div className="sec-h">Windows on this date</div>
        {/* the "N fit" match-count pill is not rendered — no endpoint supplies it (tests/TEST-INVENTORY.md,
            "A window match preview"); the chev is a #105 inline editor's, present but inert until that lands. */}
        <div className="list">{shown.windows.map(w => <div className="row" key={w.id.value}><div className="body">
          <div className="title">{w.name}</div><div className="meta"><span className="pill dur">{hm(w.start)}–{hm(w.end)}</span>{dimPills(w.tags)}</div>
        </div><span className="chev">›</span></div>)}</div>
        {shown.windows.length === 0 && <div className="empty">No windows — a completely blank day.</div>}
        {shown.events.length > 0 && <><div className="sec-h">Events</div><div className="list">{shown.events.map(event => <div className="row" key={event.id.value}><div className="body">
          <div className="title">{event.name}</div><div className="meta"><span className="pill dur">{hm(event.start)}–{hm(event.end)}</span>{dimPills(event.tags)}</div>
        </div></div>)}</div></>}
        <div className="sec"><button className="btn wide" disabled={busy} onClick={() => setStampOpen(true)}>Stamp a whole shape onto this date…</button></div>
        <div className="btn-row"><button className="btn" disabled={busy} onClick={() => setRangeOpen(true)}>Override a date range…</button></div>
        <div className="btn-row"><button className="btn" disabled={busy} onClick={() => setEventOpen(true)}>＋ Event</button><button className="btn" disabled={busy} onClick={() => setPromoteOpen(true)}>Save as a shape</button></div>
        {shown.isOverridden && <div className="btn-row"><button className="btn danger wide" disabled={busy} onClick={() => void revert()}>Put it back on the pattern</button></div>}
        <div className="note">Stamping copies the windows in. It is <b>not</b> a link — edit the shape tomorrow and this date will not follow.</div>
      </>}
    </div>
    {stampOpen && <OverrideStampSheet date={selectedDate} mutationError={error} onCancel={() => setStampOpen(false)} onStamp={stamp} busy={busy} />}
    {rangeOpen && <OverrideRangeSheet mutationError={error} date={selectedDate} busy={busy} onCancel={() => setRangeOpen(false)} onCreate={createRange} />}
    {promoteOpen && shown && <OverridePromoteSheet mutationError={error} day={shown} label={label ?? 'One-off day'} busy={busy} onCancel={() => setPromoteOpen(false)} onPromote={promote} />}
    {eventOpen && shown && <EventCreateSheet date={selectedDate} windows={shown.windows} onCancel={() => setEventOpen(false)} onCreated={() => { setEventOpen(false); setRevision(value => value + 1) }} />}
    {presentation}
  </>
}
