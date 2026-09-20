import { useEffect, useState } from 'react'
import { getJson } from '../api/client'
import type { components } from '../api/schema'
import { OverrideSheet } from './OverrideSheet'
import { DateEntry } from './shared/DateEntry'
import { fmtShort, hm } from './OverrideFormat'

type Template = components['schemas']['DayTemplateResponse']
type Window = components['schemas']['AvailabilityWindow']
type Pattern = components['schemas']['PatternResponse']
type ViewMode = 'strip' | 'pills'
type Scope = 'date' | 'range'

function minutesOf(time: string): number {
  return Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
}

// Ported from schedule-editing.prototype.html's strip() (~921) in its read-only form — the
// tappable `edit` form is the inline window editor, which is #105's, not this ticket's.
function Strip({ windows }: { windows: Window[] }) {
  const A = 6 * 60, B = 23 * 60, span = B - A
  const clamp = (percent: number) => Math.min(100, Math.max(0, percent))
  return <div className={`strip${windows.length ? '' : ' empty-d'}`}>
    {/* left/width are minutes-of-day positioned onto the 6a-11p strip — data, not design;
        index.css's `.strip u`/`.strip i` supply color, height and radius. */}
    {[9, 12, 15, 18, 21].map(h => <u key={h} style={{ left: `${(h * 60 - A) / span * 100}%` }} />)}
    {windows.flatMap(w => {
      // Clamp both ends to the track before deriving width — a window wholly outside 6a-11p
      // clamps both ends to the same edge and is skipped rather than drawn as a phantom sliver or
      // a bar spilling past the track. The 1.2 floor applies only once a bar is known non-empty.
      const left = clamp((minutesOf(w.start) - A) / span * 100)
      const right = clamp((minutesOf(w.end) - A) / span * 100)
      if (right <= left) return []
      return [<i key={w.id.value} style={{ left: `${left}%`, width: `${Math.max(1.2, right - left)}%` }} />]
    })}
  </div>
}

function ViewToggle({ view, onChange }: { view: ViewMode; onChange: (view: ViewMode) => void }) {
  return <span className="vtog">
    <button aria-pressed={view === 'strip'} onClick={() => onChange('strip')}>Strips</button>
    <button aria-pressed={view === 'pills'} onClick={() => onChange('pills')}>Times</button>
  </span>
}

// Ported from shapeRow() (~946). `isCurrent` is always false here — see the inventory note below
// this component: the screen has no template id to compare against, only a saved name.
function ShapeRow({ template, view, busy, onPick }: { template: Template; view: ViewMode; busy: boolean; onPick: () => void }) {
  // Windows compare as a multiset (DateOverride.cs) and arrive unsorted (OverrideEndpoints.cs), so
  // the summary derives the earliest start and the latest end independently rather than trusting
  // authoring order — with overlap, the last-by-start window is not necessarily the one that ends
  // last.
  const earliestStart = template.windows.reduce((min, w) => minutesOf(w.start) < minutesOf(min.start) ? w : min, template.windows[0])
  const latestEnd = template.windows.reduce((max, w) => minutesOf(w.end) > minutesOf(max.end) ? w : max, template.windows[0])
  return <button className="pickrow" aria-pressed={false} disabled={busy} onClick={onPick}>
    <span className="who">
      <span className="nm">{template.name}</span>
      <span className="sub2">{template.windows.length
        ? `${template.windows.length} window${template.windows.length === 1 ? '' : 's'} · ${hm(earliestStart.start)}–${hm(latestEnd.end)}`
        : 'no windows — a deliberately silent day'}</span>
      {view === 'strip' ? <Strip windows={template.windows} /> : <span className="meta">
        {template.windows.length
          ? template.windows.map(w => <span key={w.id.value} className="pill dur">{hm(w.start)}–{hm(w.end)}</span>)
          : <span className="pill ghost">silent</span>}
      </span>}
    </span>
  </button>
}

export function OverrideStampSheet({ date, onCancel, onStamp, busy, mutationError }: {
  date: string; onCancel: () => void
  // Every call states its arm explicitly (#145) — 'stamp' carries a templateId, 'freeze' and
  // 'blank' never do; mode is what tells those last two apart.
  onStamp: (templateId: string | null, span: { from: string; to: string } | null, mode: 'stamp' | 'freeze' | 'blank') => Promise<void>
  busy: boolean
  mutationError?: string
}) {
  const [templates, setTemplates] = useState<Template[] | null>(null)
  // null: no season to group by — still loading, the read failed, or no Pattern is marked active.
  // The picker then renders one ungrouped list rather than an error; a picker that cannot group is
  // still usable. See the inventory note.
  const [activeDays, setActiveDays] = useState<string[] | null>(null)
  const [view, setView] = useState<ViewMode>('strip')
  const [error, setError] = useState('')
  const [scope, setScope] = useState<Scope>('date')
  const [from, setFrom] = useState<string | null>(date)
  const [to, setTo] = useState<string | null>(date)
  useEffect(() => {
    let current = true
    getJson<Template[]>('/api/day-templates').then(value => { if (current) setTemplates(value ?? []) })
      .catch(reason => { if (current) setError(String(reason)) })
    // GET /api/patterns is the read that exists; GET /api/patterns/active does not — that route is
    // PUT-only (PatternEndpoints.cs:31, `get?: never` in schema.d.ts). PatternResponse now carries
    // `active` (#143), so grouping works once one Pattern is marked active.
    getJson<Pattern[]>('/api/patterns')
      .then(value => { if (current) setActiveDays(value?.find(p => p.active)?.days ?? null) })
      .catch(() => { if (current) setActiveDays(null) })
    return () => { current = false }
  }, [])
  const groups: { label: string | null; list: Template[] }[] = templates === null ? [] : activeDays === null
    ? [{ label: null, list: templates }]
    : [
      { label: 'Already in this season', list: templates.filter(t => activeDays.includes(t.id)) },
      { label: 'Used by other seasons', list: templates.filter(t => !activeDays.includes(t.id) && !t.unused) },
      { label: 'Not in use', list: templates.filter(t => !activeDays.includes(t.id) && t.unused) },
    ].filter(g => g.list.length > 0)
  const inRange = scope === 'range'
  const invalidRange = inRange && (from === null || to === null || from > to)
  const span = from !== null && to !== null ? { from, to } : null
  const rowsDisabled = busy || invalidRange
  const title = inRange ? `${fmtShort(from ?? date)} – ${fmtShort(to ?? date)}` : fmtShort(date)
  let toggleShown = false
  return <OverrideSheet title={title} onCancel={onCancel} busy={busy}>
    {mutationError && <div className="note" role="alert">{mutationError}</div>}
    <div className="modebar">
      <button aria-pressed={scope === 'date'} disabled={busy} onClick={() => setScope('date')}>This date</button>
      <button aria-pressed={scope === 'range'} disabled={busy} onClick={() => setScope('range')}>A range…</button>
    </div>
    {inRange && <>
      <DateEntry label="From" value={from} onChange={setFrom} disabled={busy} />
      <DateEntry label="To" value={to} onChange={setTo} disabled={busy} />
    </>}
    {invalidRange && <div className="note" role="alert">Choose a start and end date; the end must not precede the start.</div>}
    {error && <div className="note" role="alert">{error}</div>}
    {inRange && <>
      {/* Freeze (mode: 'freeze') copies each date's current WHOLE SHAPE — windows and events —
          into its own Override. CreateOverrideSpan's Freeze arm reads any existing Override's
          Windows and Events first, falling back to the active Pattern's template (its Windows and
          its EventPrototypes materialised for the date) only for whichever half is missing, so
          nothing on the span is ever replaced and the row carries no destructive marker. #153
          closed the gap this comment used to warn about: recurring events used to resolve from
          the active Pattern unconditionally (DayShapeReader.For), so a later Pattern switch still
          reached the events on an otherwise-frozen date — DateOverride now carries its own Events,
          and DayShapeReader consults them, so a freeze reaches both halves. */}
      <div className="sec-h">Detach the span</div>
      <div className="list"><button className="pickrow" disabled={rowsDisabled} onClick={() => void onStamp(null, span, 'freeze')}>
        <span className="who">
          <span className="nm">Keep each date's own shape</span>
          <span className="sub2">each date keeps the shape it has now, and later Pattern edits will not reach them</span>
        </span></button></div>
      {/* Blank (mode: 'blank') writes a zero-window Override for every date in the span. That
          removes recurring windows, but dated one-off Events remain, so the caption deliberately
          makes no claim that nothing will fire. */}
      <div className="sec-h">Clear the span</div>
      <div className="list"><button className="pickrow" disabled={rowsDisabled} onClick={() => void onStamp(null, span, 'blank')}>
        <span className="who">
          <span className="nm">Blank every date in the span<span className="pill due">destructive</span></span>
          <span className="sub2">every window on those dates is removed</span>
        </span></button></div>
    </>}
    {/* Captions the shape list, and must stay below the freeze and blank rows. In range scope the
        sheet offers three arms and only this one stamps — at the top of the sheet the sentence
        reads as the sheet's own instruction and is then false for the two rows above. Date scope
        renders neither of those rows, so it is the first thing under the modebar there either way. */}
    <div className="note">Stamp a shape onto {inRange ? 'every date in the span' : 'this date'}. It copies the shape in — <b>not</b> a link, so editing the shape later will not follow.</div>
    {groups.map(group => {
      const withTog = !toggleShown
      toggleShown = true
      return <div key={group.label ?? 'all'}>
        <div className={`sec-h${withTog ? ' with-tog' : ''}`}>{group.label ?? 'Shapes'}{withTog && <ViewToggle view={view} onChange={setView} />}</div>
        <div className="list">{group.list.map(t => <ShapeRow key={t.id} template={t} view={view} busy={rowsDisabled} onPick={() => void onStamp(t.id, inRange ? span : null, 'stamp')} />)}</div>
      </div>
    })}
    {templates?.length === 0 && <div className="empty">No shapes available.</div>}
    {templates && templates.length > 0 && <div className="note">{templates.length} shapes. Grouping by use keeps the one you want near the top without a search box — the shape you need on a Tuesday is nearly always one this season already contains.</div>}
  </OverrideSheet>
}
