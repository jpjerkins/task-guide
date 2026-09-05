interface OrdinalSliderProps {
  label: string
  // Ordered, least-constrained first — the caller supplies the order; this component only
  // indexes into it.
  values: readonly string[]
  value: string | null
  onChange: (next: string | null) => void
  // The Dimension's declared default. Absent means the axis declares none (Duration).
  defaultValue?: string | null
  readOnly?: boolean
  id?: string
}

// A slider over an ordered value set (CONTEXT.md 339-482: Ordinal Dimensions). "An ordinal slider
// needs an explicit control for absence. A Dimension declaring a default makes unset and
// deliberately set to the default two different states of the record, and a slider has no
// position for the first." So when a default is declared, a separate "leave at the default"
// control sits above the slider; choosing it sets value to null. When no default is declared
// (Duration), that control is absent and an unset value is simply blank.
export function OrdinalSlider({ label, values, value, onChange, defaultValue, readOnly, id }: OrdinalSliderProps) {
  const hasDefault = defaultValue !== undefined && defaultValue !== null
  const index = value === null ? -1 : values.indexOf(value)
  const sliderIndex = index === -1 ? 0 : index
  const displayLabel = value ?? (hasDefault ? defaultValue : '—')

  return (
    <div className="stack">
      {hasDefault && (
        <label className="row">
          <input
            type="checkbox"
            aria-label="Leave at the default"
            checked={value === null}
            disabled={readOnly}
            onChange={(e) => onChange(e.target.checked ? null : (defaultValue as string))}
          />
          <span className="body">Leave at the default ({defaultValue})</span>
        </label>
      )}
      <div className="stack">
        <label className="stack">
          <span className="lbl">{label}</span>
          <input
            className="field"
            type="range"
            id={id}
            min={0}
            max={values.length - 1}
            step={1}
            value={sliderIndex}
            disabled={readOnly}
            onChange={(e) => onChange(values[Number(e.target.value)])}
          />
        </label>
        <span className="body">{displayLabel}</span>
      </div>
    </div>
  )
}
