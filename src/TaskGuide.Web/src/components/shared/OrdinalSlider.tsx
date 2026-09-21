import { useRef } from 'react'

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

const RANGE_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown']

// A slider over an ordered value set (CONTEXT.md 339-482: Ordinal Dimensions), matching the
// settled shape from tag-entry.prototype.html variant C's sheet (~850-862): "An ordinal slider
// needs an explicit control for absence. A Dimension declaring a default makes unset and
// deliberately set to the default two different states of the record, and a slider has no
// position for the first. That extra control is the finding." So when a default is declared, a
// "Leave at the default (<default>)" toggle sits above the slider — an aria-pressed button in a
// chipset, not a checkbox, matching the prototype and our existing `.chipset button[aria-pressed]`
// styling. Clicking it always commits null (it is an action, like the prototype's, not a
// two-state toggle you press again to undo). While unset, the slider dims (a class, not inline
// style) and shows index 0. Duration declares no default, so that control is simply absent.
export function OrdinalSlider({ label, values, value, onChange, defaultValue, readOnly, id }: OrdinalSliderProps) {
  const pointerStartedOnSlider = useRef(false)
  const keyStartedOnSlider = useRef(false)
  const changedDuringKeypress = useRef(false)
  const hasDefault = defaultValue !== undefined && defaultValue !== null
  // A value not present in `values` (indexOf === -1) falls back to the unset presentation too —
  // otherwise React writes value="-1" on the input, the browser clamps the visible thumb to
  // index 0, but the dimming/hint logic below (keyed on `value === null`) wouldn't know that
  // happened and would claim "Set to <the missing value>" over a control showing something else.
  const rawIndex = value === null ? -1 : values.indexOf(value)
  const unset = rawIndex === -1
  const index = unset ? 0 : rawIndex

  let hint: string
  if (unset) {
    hint = hasDefault
      ? `Nothing chosen — the slider is showing ${values[0]} but the task carries the default. Touch it to commit a value.`
      : 'Not set.'
  } else {
    hint = `Set to ${value}.`
  }

  return (
    <div className="stack">
      <div className="lbl">{label}</div>
      {hasDefault && (
        <div className="chipset">
          <button type="button" aria-pressed={unset} disabled={readOnly} onClick={() => onChange(null)}>
            Leave at the default ({defaultValue})
          </button>
        </div>
      )}
      <input
        className={unset ? 'range unset' : 'range'}
        type="range"
        aria-label={label}
        id={id}
        min={0}
        max={values.length - 1}
        step={1}
        value={index}
        disabled={readOnly}
        onChange={(e) => {
          changedDuringKeypress.current = true
          onChange(values[Number(e.target.value)])
        }}
        // While unset, the thumb sits at index 0 — a decrementing key (ArrowLeft, ArrowDown,
        // Home, PageDown) is a no-op there and the browser fires no `change` for it, so it would
        // commit nothing. The rule is: a keystroke that started on this control, moved the thumb
        // nowhere, and is a key a range input actually responds to commits the value being shown.
        // Provenance is deliberately not keyed off which key it was (#147, 2nd pass) — a key list
        // can always be incomplete, and `keyup` fires on whatever element is focused AT keyup
        // time, not keydown time, so `Tab` and `Shift+Tab` both land a keyup on a slider whose
        // keydown happened on the control being left (Shift is released last, so it fires on
        // essentially every backward Tab traversal). Naming those keys would just repeat the
        // mistake; tracking where the keydown landed subsumes them without knowing their names,
        // mirroring `pointerStartedOnSlider` below. But Tab also moves focus ON keydown, so a
        // keydown here can be followed by the keyup landing on the NEXT control instead of this
        // one — this slider's keyup never runs, and the ref would stay stale until a later,
        // unrelated keyup found it still `true`. Clearing it on blur closes that gap, mirroring
        // `onPointerCancel`'s guard on the pointer path. `changedDuringKeypress` skips the reset
        // on an auto-repeated keydown (`e.repeat`) so a held key doesn't erase the record of a
        // `change` that already fired earlier in the same keystroke. And the key itself must be
        // one from RANGE_KEYS — a keystroke can start and end on this control without moving the
        // thumb because it was never aimed at the slider at all (Enter to submit the form, Escape
        // to dismiss, Ctrl+S), and provenance alone can't tell that apart from a real no-op arrow
        // press. This is knowingly a key list again, the failure mode #147 opened with — an
        // omitted key silently can't commit — but here the list is exhaustive over what a range
        // input responds to at all, not a guess at which keys decrement.
        onKeyDown={() => {
          if (!keyStartedOnSlider.current) {
            changedDuringKeypress.current = false
          }
          keyStartedOnSlider.current = true
        }}
        onKeyUp={(e) => {
          if (
            unset &&
            keyStartedOnSlider.current &&
            !changedDuringKeypress.current &&
            RANGE_KEYS.includes(e.key) &&
            !e.ctrlKey &&
            !e.altKey &&
            !e.metaKey
          ) {
            onChange(values[index])
          }
          keyStartedOnSlider.current = false
        }}
        onBlur={() => {
          keyStartedOnSlider.current = false
          changedDuringKeypress.current = false
        }}
        // While unset, the thumb already sits at index 0 — dragging it TO 0 fires no `change`
        // event, so a user could never explicitly commit the least value. A pointerUp (covers a
        // click too, and a touch drag's release) commits whatever the slider is currently
        // showing. Once a value is set, this is a no-op: `change` already owns every further
        // commit, and firing again here would be redundant, not wrong, but the guard keeps the
        // handler's job to exactly "commit from unset" and nothing else.
        onPointerDown={() => {
          pointerStartedOnSlider.current = true
        }}
        onPointerUp={() => {
          if (unset && pointerStartedOnSlider.current) {
            onChange(values[index])
          }
          pointerStartedOnSlider.current = false
        }}
        onPointerCancel={() => {
          pointerStartedOnSlider.current = false
        }}
      />
      <div className="ticks">
        {values.map((v) => (
          <span key={v}>{v}</span>
        ))}
      </div>
      <div className="hint">{hint}</div>
    </div>
  )
}
