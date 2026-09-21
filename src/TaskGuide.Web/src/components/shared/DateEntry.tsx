interface DateEntryProps {
  value: string | null
  onChange: (next: string | null) => void
  label: string
  id?: string
  disabled?: boolean
  /** Caps the input's width to the tag-entry prototype's date field. Off by default: every other
   * call site follows a prototype that leaves the field full-width. */
  narrow?: boolean
}

// Wraps a native <input type="date">. ADR-0006: a system-presented control must survive its own
// input events — the remount is what dismisses a native picker mid-interaction on iOS Safari. So
// this component keeps a stable key, never swaps to a different element in response to its own
// change, and only ever reassigns `value` on the same node.
export function DateEntry({ value, onChange, label, id, disabled, narrow }: DateEntryProps) {
  return (
    <label className="stack">
      <span className="lbl">{label}</span>
      <input
        className={narrow ? 'field date' : 'field'}
        type="date"
        id={id}
        value={value ?? ''}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
      />
    </label>
  )
}
