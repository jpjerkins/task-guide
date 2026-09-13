import { useEffect, useState } from 'react'
import { getJson } from '../api/client'
import type { components } from '../api/schema'
import type { OverrideSpan } from './OverrideRange'
import { DateEntry } from './shared/DateEntry'
import { OverrideSheet } from './OverrideSheet'

export function OverrideRangeSheet({ date, busy, onCancel, onCreate, mutationError }: {
  date: string; busy: boolean; onCancel: () => void; onCreate: (span: OverrideSpan) => Promise<void>
  mutationError?: string
}) {
  const [from, setFrom] = useState<string | null>(date)
  const [to, setTo] = useState<string | null>(date)
  const [templateId, setTemplateId] = useState('')
  const [templates, setTemplates] = useState<components['schemas']['DayTemplateResponse'][]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    let current = true
    getJson<components['schemas']['DayTemplateResponse'][]>('/api/day-templates')
      .then(value => { if (current) setTemplates(value ?? []) })
      .catch(reason => { if (current) setError(String(reason)) })
    return () => { current = false }
  }, [])
  const valid = from !== null && to !== null && from <= to
  return <OverrideSheet title="Override a date range" onCancel={onCancel} busy={busy}>
    {mutationError && <div className="note" role="alert">{mutationError}</div>}
    <div className="stack">
      <DateEntry label="Start date" value={from} onChange={setFrom} disabled={busy} />
      <DateEntry label="End date" value={to} onChange={setTo} disabled={busy} />
      <label className="lbl" htmlFor="override-template">Day template</label>
      <select className="field" id="override-template" value={templateId} disabled={busy} onChange={event => setTemplateId(event.target.value)}>
        <option value="">Copy each date’s current shape</option>
        {templates.map(template => <option key={template.id} value={template.id}>{template.name}</option>)}
      </select>
    </div>
    {!valid && <div className="note" role="alert">Choose a start and end date; the end must not precede the start.</div>}
    {error && <div className="note" role="alert">{error}</div>}
    <div className="note">Each date keeps its own copy of the windows. This stamps a shape — it does not create a link.</div>
    <div className="btn-row"><button className="btn primary wide" disabled={busy || !valid} onClick={() => {
      if (valid) void onCreate({ from, to, templateId: templateId || null })
    }}>Create Overrides</button></div>
  </OverrideSheet>
}
