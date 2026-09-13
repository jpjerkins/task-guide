import { useEffect, useState } from 'react'
import { getJson } from '../api/client'
import type { components } from '../api/schema'
import { OverrideSheet } from './OverrideSheet'

type Template = components['schemas']['DayTemplateResponse']
export function OverrideStampSheet({ onCancel, onStamp, busy, mutationError }: {
  onCancel: () => void; onStamp: (id: string) => Promise<void>; busy: boolean
  mutationError?: string
}) {
  const [templates, setTemplates] = useState<Template[] | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let current = true
    getJson<Template[]>('/api/day-templates').then(value => { if (current) setTemplates(value ?? []) })
      .catch(reason => { if (current) setError(String(reason)) })
    return () => { current = false }
  }, [])
  return <OverrideSheet title="Stamp a shape" onCancel={onCancel} busy={busy}>
    {mutationError && <div className="note" role="alert">{mutationError}</div>}
    <div className="note">Stamp a shape onto this date. It copies the windows in — <b>not</b> a link, so editing the shape later will not follow.</div>
    {error && <div className="note" role="alert">{error}</div>}
    <div className="list">{templates?.map(template => <button className="pickrow" key={template.id} disabled={busy} onClick={() => void onStamp(template.id)}>
      <span className="who"><span className="nm">{template.name}</span><span className="sub2">{template.windows.length ? `${template.windows.length} window${template.windows.length === 1 ? '' : 's'}` : 'no windows — a deliberately silent day'}</span><span className="meta">{template.windows.map(w => <span key={w.id.value} className="pill dur">{w.start.slice(0, 5)}–{w.end.slice(0, 5)}</span>)}</span></span>
    </button>)}</div>
    {templates?.length === 0 && <div className="empty">No shapes available.</div>}
  </OverrideSheet>
}
