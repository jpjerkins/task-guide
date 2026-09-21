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

// A slider over an ordered value set (CONTEXT.md 339-482: Ordinal Dimensions), matching the
// settled shape from tag-entry.prototype.html variant C's sheet (~850-862): "An ordinal slider
// needs an explicit control for absence. A Dimension declaring a default makes unset and
// deliberately set to the default two different states of the record, and a slider has no
// position for the first. That extra control is the finding." So when a default is declared, a
// "Leave at the default (<default>)" toggle sits above the slider — an aria-pressed button in a
// chipset, not a checkbox, matching the prototype and our existing `.chipset button[aria-pressed]`
// styling. Clicking it always commits null (it is an action, like the prototype's, not a
// two-state toggle you press again to undo). While unset, the slider dims (a class, not inline
// style) and shows index 0. The "Leave at the default" toggle is absent without a declared
// default, but the chipset still renders while unset to carry the "Use <least value>" button
// (#147) — the only way a no-default (Duration) slider can ever commit its least value.
export function OrdinalSlider({ label, values, value, onChange, defaultValue, readOnly, id }: OrdinalSliderProps) {
  const pointerStartedOnSlider = useRef(false)
  const changedDuringGesture = useRef(false)
  const inputRef = useRef<HTMLInputElement>(null)
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
      ? `Nothing chosen — the slider is showing ${values[0]} but the task carries the default. Drag it, or press "Use ${values[0]}", to commit a value.`
      : `Not set. Drag the slider, or press "Use ${values[0]}", to commit a value.`
  } else {
    hint = `Set to ${value}.`
  }

  return (
    <div className="stack">
      <div className="lbl">{label}</div>
      {(hasDefault || unset) && (
        // Five review rounds tried to make a keyboard keystroke commit an unset slider and each
        // found another key/ordering that fired wrongly — #147's final call was to delete that
        // path and replace it with this button, keyboard-reachable by construction. It's an
        // authoring affordance only: readOnly viewers (DimensionsScreen) can't act on it, so it's
        // suppressed there rather than shown disabled and dead.
        <div className="chipset">
          {hasDefault && (
            <button type="button" aria-pressed={unset} disabled={readOnly} onClick={() => onChange(null)}>
              Leave at the default ({defaultValue})
            </button>
          )}
          {unset && !readOnly && (
            <button
              type="button"
              onClick={() => {
                onChange(values[0])
                inputRef.current?.focus()
              }}
            >
              Use {values[0]}
            </button>
          )}
        </div>
      )}
      <input
        ref={inputRef}
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
          changedDuringGesture.current = true
          onChange(values[Number(e.target.value)])
        }}
        // While unset, the thumb already sits at index 0 — dragging it TO 0 fires no `change`
        // event, so a user could never explicitly commit the least value. A pointerUp (covers a
        // click too, and a touch drag's release) commits whatever the slider is currently
        // showing. Once a value is set, this is a no-op: `change` already owns every further
        // commit, and firing again here would be redundant, not wrong, but the guard keeps the
        // handler's job to exactly "commit from unset" and nothing else. `changedDuringGesture`
        // guards against a parent that defers applying `onChange` (an awaited write, a
        // transition): a click at index 2 fires `change` -> onChange('normal'), but if the parent
        // hasn't re-rendered with the new value yet, `unset` here is still true and pointerUp
        // would fire a second, wrong onChange(values[0]) on top of it.
        onPointerDown={() => {
          // Only a fresh gesture (no prior pointerdown pending) resets the guard — a second
          // pointerdown mid-gesture (a stray pointer, a second finger) must not clear it, or it
          // defeats the downgrade guard below the same way the keyboard path's old bugs did.
          if (!pointerStartedOnSlider.current) {
            changedDuringGesture.current = false
          }
          pointerStartedOnSlider.current = true
        }}
        onPointerUp={() => {
          if (unset && pointerStartedOnSlider.current && !changedDuringGesture.current) {
            onChange(values[index])
          }
          pointerStartedOnSlider.current = false
          changedDuringGesture.current = false
        }}
        onPointerCancel={() => {
          pointerStartedOnSlider.current = false
          changedDuringGesture.current = false
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
