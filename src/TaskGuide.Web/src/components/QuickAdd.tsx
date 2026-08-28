import { useState } from 'react'

const DURATIONS = [2, 10, 30, 60]

interface QuickAddProps {
  onCancel: () => void
  onAdd: (title: string, duration: number) => void | Promise<void>
}

// The duration chip IS the submit — matches docs/prototypes/ui-screens.prototype.html's
// captureSheet (`data-act="capture"` fires straight off the chip tap). There is no
// separate confirm button. A chip is inert until a title has been entered.
export function QuickAdd({ onCancel, onAdd }: QuickAddProps) {
  const [title, setTitle] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const canSubmit = title.trim().length > 0 && !submitting

  async function submit(duration: number) {
    if (!canSubmit) return
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
              <button key={d} disabled={!canSubmit} onClick={() => submit(d)}>
                {d}m
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
