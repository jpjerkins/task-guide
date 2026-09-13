import { useEffect, useRef, type ReactNode } from 'react'

// The prototype's veil > sheet > grabber/sheet-h structure, shared within this ticket.
export function OverrideSheet({ title, children, onCancel, busy = false }: {
  title: string; children: ReactNode; onCancel: () => void; busy?: boolean
}) {
  const root = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const previous = document.activeElement
    root.current?.focus()
    return () => { if (previous instanceof HTMLElement && previous.isConnected) previous.focus() }
  }, [])
  return <div ref={root} tabIndex={-1} onKeyDown={event => {
    if (event.key === 'Tab') {
      const controls = event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href]')
      const first = controls[0]
      const last = controls[controls.length - 1]
      if (!first) { event.preventDefault(); return }
      if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    if (event.key === 'Escape' && !busy) { event.stopPropagation(); onCancel() }
  }} className="veil" role="dialog" aria-modal="true" aria-label={title} onClick={() => { if (!busy) onCancel() }}>
    <div className="sheet" onClick={event => event.stopPropagation()}>
      <div className="grabber" />
      <div className="sheet-h"><h2>{title}</h2><button className="icon" disabled={busy} onClick={onCancel}>Cancel</button></div>
      {children}
    </div>
  </div>
}
