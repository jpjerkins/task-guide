import { useState } from 'react'
import type { components } from '../api/schema'
import { OverrideSheet } from './OverrideSheet'

export function OverridePromoteSheet({ day, busy, onCancel, onPromote, mutationError }: {
  day: components['schemas']['DayShape']; busy: boolean; onCancel: () => void; onPromote: (name: string) => Promise<void>
  mutationError?: string
}) {
  const [name, setName] = useState('')
  return <OverrideSheet title="Save this day as a shape" busy={busy} onCancel={onCancel}>
    {mutationError && <div className="note" role="alert">{mutationError}</div>}
    <div className="stack"><label className="lbl" htmlFor="override-shape-name">Call it</label>
      <input className="field" id="override-shape-name" value={name} disabled={busy} onChange={event => setName(event.target.value)} />
      <div className="lbl">Windows it will carry</div>
    </div>
    <div className="list">{day.windows.map(w => <div className="row" key={w.id.value}><div className="body">
      <div className="title">{w.name}</div><div className="meta"><span className="pill dur">{w.start.slice(0, 5)}–{w.end.slice(0, 5)}</span></div>
    </div></div>)}</div>
    <div className="btn-row"><button className="btn primary wide" disabled={busy || !name.trim()} onClick={() => void onPromote(name.trim())}>Save the shape</button></div>
    <div className="note">{day.date} <b>does not re-link</b> to the new shape — it keeps its own copy. A shape is a thing you can reach for again, not a thread back to the day it came from.</div>
  </OverrideSheet>
}
