import { useEffect, useState } from 'react'
import { getJson } from '../api/client'
import type { components } from '../api/schema'
import { OverrideSheet } from './OverrideSheet'
import { fmtShort, hm } from './OverrideFormat'

type Template = components['schemas']['DayTemplateResponse']
type Window = components['schemas']['AvailabilityWindow']
type ViewMode = 'strip' | 'pills'

function minutesOf(time: string): number {
  return Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
}

// Ported from schedule-editing.prototype.html's strip() (~921) in its read-only form — the
// tappable `edit` form is the inline window editor, which is #105's, not this ticket's.
function Strip({ windows }: { windows: Window[] }) {
  const A = 6 * 60, B = 23 * 60, span = B - A
  return <div className={`strip${windows.length ? '' : ' empty-d'}`}>
    {/* left/width are minutes-of-day positioned onto the 6a-11p strip — data, not design;
        index.css's `.strip u`/`.strip i` supply color, height and radius. */}
    {[9, 12, 15, 18, 21].map(h => <u key={h} style={{ left: `${(h * 60 - A) / span * 100}%` }} />)}
    {windows.map(w => {
      const left = Math.max(0, (minutesOf(w.start) - A) / span * 100)
      const right = Math.min(100, (minutesOf(w.end) - A) / span * 100)
      return <i key={w.id.value} style={{ left: `${left}%`, width: `${Math.max(1.2, right - left)}%` }} />
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
  const first = template.windows[0]
  const last = template.windows[template.windows.length - 1]
  return <button className="pickrow" aria-pressed={false} disabled={busy} onClick={onPick}>
    <span className="who">
      <span className="nm">{template.name}</span>
      <span className="sub2">{template.windows.length
        ? `${template.windows.length} window${template.windows.length === 1 ? '' : 's'} · ${hm(first.start)}–${hm(last.end)}`
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
  date: string; onCancel: () => void; onStamp: (id: string) => Promise<void>; busy: boolean
  mutationError?: string
}) {
  const [templates, setTemplates] = useState<Template[] | null>(null)
  // null: no season to group by, either still loading or the read failed — the picker then
  // renders one ungrouped list rather than an error (there is no GET for this today; see the
  // inventory note below).
  const [activeDays, setActiveDays] = useState<string[] | null>(null)
  const [view, setView] = useState<ViewMode>('strip')
  const [error, setError] = useState('')
  useEffect(() => {
    let current = true
    getJson<Template[]>('/api/day-templates').then(value => { if (current) setTemplates(value ?? []) })
      .catch(reason => { if (current) setError(String(reason)) })
    getJson<components['schemas']['PatternResponse']>('/api/patterns/active')
      .then(value => { if (current) setActiveDays(value?.days ?? null) })
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
  let toggleShown = false
  return <OverrideSheet title={fmtShort(date)} onCancel={onCancel} busy={busy}>
    {mutationError && <div className="note" role="alert">{mutationError}</div>}
    <div className="note">Stamp a shape onto this date. It copies the windows in — <b>not</b> a link, so editing the shape later will not follow.</div>
    {error && <div className="note" role="alert">{error}</div>}
    {groups.map(group => {
      const withTog = !toggleShown
      toggleShown = true
      return <div key={group.label ?? 'all'}>
        <div className={`sec-h${withTog ? ' with-tog' : ''}`}>{group.label ?? 'Shapes'}{withTog && <ViewToggle view={view} onChange={setView} />}</div>
        <div className="list">{group.list.map(t => <ShapeRow key={t.id} template={t} view={view} busy={busy} onPick={() => void onStamp(t.id)} />)}</div>
      </div>
    })}
    {templates?.length === 0 && <div className="empty">No shapes available.</div>}
    {templates && templates.length > 0 && <div className="note">{templates.length} shapes. Grouping by use keeps the one you want near the top without a search box — the shape you need on a Tuesday is nearly always one this season already contains.</div>}
  </OverrideSheet>
}
