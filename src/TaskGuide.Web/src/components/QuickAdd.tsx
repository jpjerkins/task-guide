import { useState } from 'react'

const DURATIONS = [2, 10, 30, 60]

interface QuickAddProps {
  onCancel: () => void
  onAdd: (title: string, duration: number) => void | Promise<void>
}

export function QuickAdd({ onCancel, onAdd }: QuickAddProps) {
  const [title, setTitle] = useState('')
  const [duration, setDuration] = useState<number | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const canSubmit = title.trim().length > 0 && duration !== null && !submitting

  async function submit() {
    if (!canSubmit || duration === null) return
    setSubmitting(true)
    try {
      await onAdd(title.trim(), duration)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="veil" onClick={onCancel}>
      <div className="sheet" onClick={(e) => e.stopPropagation()}>
        <div className="grabber" />
        <div className="sheet-h">
          <h2>Quick add</h2>
          <button className="icon" onClick={onCancel}>
            Cancel
          </button>
        </div>
        <div className="stack">
          <input
            className="field"
            placeholder="What is it?"
            autoFocus
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
          <div className="lbl">How long?</div>
          <div className="chipset">
            {DURATIONS.map((d) => (
              <button
                key={d}
                aria-pressed={duration === d}
                onClick={() => setDuration(d)}
              >
                {d}m
              </button>
            ))}
          </div>
          <button className="btn primary wide" disabled={!canSubmit} onClick={submit}>
            Add
          </button>
        </div>
      </div>
    </div>
  )
}
