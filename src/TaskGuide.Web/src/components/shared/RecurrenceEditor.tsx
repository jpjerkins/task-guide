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

// The closed set above, rendered as one <select> plus every sub-field group mounted once and
// toggled with `hidden`. ADR-0006 requires this: switching kind changes which sub-fields apply,
// and a conditional-render branch swap here would replace the <select>'s siblings and risk an
// ancestor rebuild that dismisses a control mid-interaction. The <select> node itself, and every
// sub-field's node, stays identical across a kind change.
export function RecurrenceEditor({ value, onChange, firstDue, onFirstDueChange, disabled }: RecurrenceEditorProps) {
  const selection = selectionOf(value)

  function handleKindChange(next: string) {
    onChange(defaultFor(next as Selection))
  }

  function handleNChange(next: number) {
    if (hasN(value)) {
      onChange({ ...value, n: next })
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

  function handleDayOfMonthChange(next: number) {
    if (value && value.kind === 'monthlyOnDayOfMonth') {
      onChange({ ...value, dayOfMonth: next })
    }
  }

  function handleMonthChange(next: number) {
    if (value && value.kind === 'yearlyOnMonthDay') {
      onChange({ ...value, month: next })
    }
  }

  function handleYearlyDayChange(next: number) {
    if (value && value.kind === 'yearlyOnMonthDay') {
      onChange({ ...value, dayOfMonth: next })
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

      <div hidden={!hasN(value)} className="stack">
        <label className="stack">
          <span className="lbl">Every N</span>
          <input
            className="field"
            type="number"
            min={1}
            disabled={disabled}
            value={hasN(value) ? value.n : 1}
            onChange={(e) => handleNChange(Number(e.target.value))}
          />
        </label>
      </div>

      <div hidden={!isEveryNWeeks} className="chipset">
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

      <div hidden={value?.kind !== 'monthlyOnDayOfMonth'} className="stack">
        <label className="stack">
          <span className="lbl">Day of month</span>
          <input
            className="field"
            type="number"
            min={1}
            max={31}
            disabled={disabled}
            value={value?.kind === 'monthlyOnDayOfMonth' ? value.dayOfMonth : 1}
            onChange={(e) => handleDayOfMonthChange(Number(e.target.value))}
          />
        </label>
      </div>

      <div hidden={value?.kind !== 'yearlyOnMonthDay'} className="stack">
        <label className="stack">
          <span className="lbl">Month</span>
          <input
            className="field"
            type="number"
            min={1}
            max={12}
            disabled={disabled}
            value={value?.kind === 'yearlyOnMonthDay' ? value.month : 1}
            onChange={(e) => handleMonthChange(Number(e.target.value))}
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
            value={value?.kind === 'yearlyOnMonthDay' ? value.dayOfMonth : 1}
            onChange={(e) => handleYearlyDayChange(Number(e.target.value))}
          />
        </label>
      </div>

      {/* A completion-anchored Task needs a start point before its first completion. */}
      <div hidden={value?.anchor !== 'completion'}>
        <DateEntry label="First due" value={firstDue} onChange={onFirstDueChange} disabled={disabled} />
      </div>
    </div>
  )
}
