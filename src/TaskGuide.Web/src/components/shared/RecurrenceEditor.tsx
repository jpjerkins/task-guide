import { useEffect, useState } from 'react'
import { DateEntry } from './DateEntry'

// Expressible recurrence rules are a closed set, two kinds: calendar-anchored (the world imposes
// the date) and completion-anchored (doing it restarts the clock). Modelled as a discriminated
// union — the repo's server-side idiom for closed sets — rather than an enum beside a nullable
// field, so a new case breaks every call site at compile time.
export type Weekday = 'mon' | 'tue' | 'wed' | 'thu' | 'fri' | 'sat' | 'sun'

export type RecurrenceRule =
  | { anchor: 'calendar'; kind: 'everyNDays'; n: number }
  | { anchor: 'calendar'; kind: 'everyNWeeks'; n: number; weekdays: Weekday[] }
  | { anchor: 'calendar'; kind: 'monthlyOnDayOfMonth'; dayOfMonth: number }
  | { anchor: 'calendar'; kind: 'yearlyOnMonthDay'; month: number; dayOfMonth: number }
  | { anchor: 'completion'; kind: 'everyNDays' | 'everyNWeeks' | 'everyNMonths'; n: number }

type Selection = 'none' | 'calendar:everyNDays' | 'calendar:everyNWeeks' | 'calendar:monthlyOnDayOfMonth' | 'calendar:yearlyOnMonthDay' | 'completion:everyNDays' | 'completion:everyNWeeks' | 'completion:everyNMonths'

const WEEKDAYS: Weekday[] = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun']

interface RecurrenceEditorProps {
  value: RecurrenceRule | null
  onChange: (next: RecurrenceRule | null) => void
  // A completion-anchored Task needs a start point before its first completion — CreatedAt,
  // unless an explicit first-due date is given. That date lives here, not inferred client-side.
  firstDue: string | null
  onFirstDueChange: (next: string | null) => void
  disabled?: boolean
}

function selectionOf(value: RecurrenceRule | null): Selection {
  return value === null ? 'none' : (`${value.anchor}:${value.kind}` as Selection)
}

function defaultFor(selection: Selection): RecurrenceRule | null {
  switch (selection) {
    case 'none':
      return null
    case 'calendar:everyNDays':
      return { anchor: 'calendar', kind: 'everyNDays', n: 1 }
    case 'calendar:everyNWeeks':
      return { anchor: 'calendar', kind: 'everyNWeeks', n: 1, weekdays: [] }
    case 'calendar:monthlyOnDayOfMonth':
      return { anchor: 'calendar', kind: 'monthlyOnDayOfMonth', dayOfMonth: 1 }
    case 'calendar:yearlyOnMonthDay':
      return { anchor: 'calendar', kind: 'yearlyOnMonthDay', month: 1, dayOfMonth: 1 }
    case 'completion:everyNDays':
      return { anchor: 'completion', kind: 'everyNDays', n: 1 }
    case 'completion:everyNWeeks':
      return { anchor: 'completion', kind: 'everyNWeeks', n: 1 }
    case 'completion:everyNMonths':
      return { anchor: 'completion', kind: 'everyNMonths', n: 1 }
  }
}

function hasN(value: RecurrenceRule | null): value is Extract<RecurrenceRule, { n: number }> {
  return value !== null && 'n' in value
}

// `Number('') === 0`, so a cleared field would otherwise commit 0 silently. And `min`/`max` on
// <input type=number> only constrain the spinner and form validation — a typed value bypasses
// both — so an explicit range check is the only thing that actually holds these fields' bounds.
// Returns null for anything that shouldn't commit; the caller then leaves the last valid value in
// place rather than writing a bad one.
function parsedInRange(raw: string, min: number, max: number): number | null {
  if (raw.trim() === '') {
    return null
  }
  const n = Number(raw)
  if (!Number.isInteger(n) || n < min || n > max) {
    return null
  }
  return n
}

// The closed set above, rendered as one <select> plus every sub-field group mounted once and
// toggled with `hidden`. ADR-0006 requires this: switching kind changes which sub-fields apply,
// and a conditional-render branch swap here would replace the <select>'s siblings and risk an
// ancestor rebuild that dismisses a control mid-interaction. The <select> node itself, and every
// sub-field's node, stays identical across a kind change.
export function RecurrenceEditor({ value, onChange, firstDue, onFirstDueChange, disabled }: RecurrenceEditorProps) {
  const selection = selectionOf(value)
  const [nDraft, setNDraft] = useState<string | null>(null)
  const [dayOfMonthDraft, setDayOfMonthDraft] = useState<string | null>(null)
  const [monthDraft, setMonthDraft] = useState<string | null>(null)
  const [yearlyDayDraft, setYearlyDayDraft] = useState<string | null>(null)

  // A parent re-render with a new committed rule wins over any locally retained, invalid draft.
  // Until then, retaining the draft lets a person clear a field and type its replacement without
  // React restoring the previous committed value ahead of each rejected keystroke.
  useEffect(() => {
    setNDraft(null)
    setDayOfMonthDraft(null)
    setMonthDraft(null)
    setYearlyDayDraft(null)
  }, [value])

  function handleKindChange(next: string) {
    const nextValue = defaultFor(next as Selection)
    if (value?.anchor === 'completion' && nextValue?.anchor !== 'completion') {
      onFirstDueChange(null)
    }
    onChange(nextValue)
  }

  function handleNChange(raw: string) {
    const n = parsedInRange(raw, 1, Infinity)
    if (n !== null && hasN(value)) {
      onChange({ ...value, n })
    }
  }

  function handleWeekdayToggle(day: Weekday) {
    if (value && value.anchor === 'calendar' && value.kind === 'everyNWeeks') {
      const weekdays = value.weekdays.includes(day)
        ? value.weekdays.filter((d) => d !== day)
        : [...value.weekdays, day]
      onChange({ ...value, weekdays })
    }
  }

  function handleDayOfMonthChange(raw: string) {
    const n = parsedInRange(raw, 1, 31)
    if (n !== null && value && value.kind === 'monthlyOnDayOfMonth') {
      onChange({ ...value, dayOfMonth: n })
    }
  }

  function handleMonthChange(raw: string) {
    const n = parsedInRange(raw, 1, 12)
    if (n !== null && value && value.kind === 'yearlyOnMonthDay') {
      onChange({ ...value, month: n })
    }
  }

  function handleYearlyDayChange(raw: string) {
    const n = parsedInRange(raw, 1, 31)
    if (n !== null && value && value.kind === 'yearlyOnMonthDay') {
      onChange({ ...value, dayOfMonth: n })
    }
  }

  const isEveryNWeeks = value?.anchor === 'calendar' && value.kind === 'everyNWeeks'

  return (
    <div className="stack">
      <label className="stack">
        <span className="lbl">Repeats</span>
        <select
          className="field"
          value={selection}
          disabled={disabled}
          onChange={(e) => handleKindChange(e.target.value)}
        >
          <option value="none">Does not repeat</option>
          <option value="calendar:everyNDays">Every N days</option>
          <option value="calendar:everyNWeeks">Every N weeks, on chosen weekdays</option>
          <option value="calendar:monthlyOnDayOfMonth">Monthly, on a day of the month</option>
          <option value="calendar:yearlyOnMonthDay">Yearly, on a month and day</option>
          <option value="completion:everyNDays">Every N days since last completion</option>
          <option value="completion:everyNWeeks">Every N weeks since last completion</option>
          <option value="completion:everyNMonths">Every N months since last completion</option>
        </select>
      </label>

      <div hidden={!hasN(value)} className="stack" data-group="everyN">
        <label className="stack">
          <span className="lbl">Every N</span>
          <input
            className="field"
            type="number"
            min={1}
            disabled={disabled}
            value={nDraft ?? (hasN(value) ? value.n : 1)}
            onChange={(e) => {
              setNDraft(e.target.value)
              handleNChange(e.target.value)
            }}
          />
        </label>
      </div>

      <div hidden={!isEveryNWeeks} className="chipset" data-group="weekdays">
        {WEEKDAYS.map((day) => (
          <button
            key={day}
            type="button"
            disabled={disabled}
            aria-pressed={isEveryNWeeks && value.weekdays.includes(day)}
            onClick={() => handleWeekdayToggle(day)}
          >
            {day}
          </button>
        ))}
      </div>

      <div hidden={value?.kind !== 'monthlyOnDayOfMonth'} className="stack" data-group="dayOfMonth">
        <label className="stack">
          <span className="lbl">Day of month</span>
          <input
            className="field"
            type="number"
            min={1}
            max={31}
            disabled={disabled}
            value={dayOfMonthDraft ?? (value?.kind === 'monthlyOnDayOfMonth' ? value.dayOfMonth : 1)}
            onChange={(e) => {
              setDayOfMonthDraft(e.target.value)
              handleDayOfMonthChange(e.target.value)
            }}
          />
        </label>
      </div>

      <div hidden={value?.kind !== 'yearlyOnMonthDay'} className="stack" data-group="monthDay">
        <label className="stack">
          <span className="lbl">Month</span>
          <input
            className="field"
            type="number"
            min={1}
            max={12}
            disabled={disabled}
            value={monthDraft ?? (value?.kind === 'yearlyOnMonthDay' ? value.month : 1)}
            onChange={(e) => {
              setMonthDraft(e.target.value)
              handleMonthChange(e.target.value)
            }}
          />
        </label>
        <label className="stack">
          <span className="lbl">Day</span>
          <input
            className="field"
            type="number"
            min={1}
            max={31}
            disabled={disabled}
            value={yearlyDayDraft ?? (value?.kind === 'yearlyOnMonthDay' ? value.dayOfMonth : 1)}
            onChange={(e) => {
              setYearlyDayDraft(e.target.value)
              handleYearlyDayChange(e.target.value)
            }}
          />
        </label>
      </div>

      {/* A completion-anchored Task needs a start point before its first completion. */}
      <div hidden={value?.anchor !== 'completion'} data-group="firstDue">
        <DateEntry label="First due" value={firstDue} onChange={onFirstDueChange} disabled={disabled} />
      </div>
    </div>
  )
}
