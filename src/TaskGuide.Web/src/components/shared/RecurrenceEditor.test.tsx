import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RecurrenceEditor } from './RecurrenceEditor'

describe('RecurrenceEditor', () => {
  it('renders no rule as "does not repeat"', () => {
    render(<RecurrenceEditor value={null} onChange={() => {}} firstDue={null} onFirstDueChange={() => {}} />)

    expect(screen.getByLabelText(/repeats/i)).toHaveValue('none')
  })

  it('changing kind to "every N weeks" calls onChange with a calendar-anchored rule', () => {
    const onChange = vi.fn()
    render(<RecurrenceEditor value={null} onChange={onChange} firstDue={null} onFirstDueChange={() => {}} />)

    fireEvent.change(screen.getByLabelText(/repeats/i), { target: { value: 'calendar:everyNWeeks' } })

    expect(onChange).toHaveBeenCalledWith({ anchor: 'calendar', kind: 'everyNWeeks', n: 1, weekdays: [] })
  })

  it('changing the N field on an everyNDays rule updates n', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNDays', n: 3 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText(/every n/i), { target: { value: '5' } })

    expect(onChange).toHaveBeenCalledWith({ anchor: 'calendar', kind: 'everyNDays', n: 5 })
  })

  // Review finding 3: `Number('') === 0`, so backspacing a number field committed `n: 0` (or
  // `dayOfMonth: 0`, etc.) with no guard at all — and `min`/`max` on <input type=number> only
  // constrain the spinner and form validation, not a typed value, so a typed 99 sailed past
  // `max={31}` too. A cleared or out-of-range field must be ignored, keeping the last valid value.
  it('emptying the "Every N" field does not commit n: 0', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNDays', n: 3 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText(/every n/i), { target: { value: '' } })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('typing an out-of-range "Day of month" does not commit it', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'monthlyOnDayOfMonth', dayOfMonth: 5 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText(/day of month/i), { target: { value: '99' } })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('typing an out-of-range "Month" does not commit it', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'yearlyOnMonthDay', month: 6, dayOfMonth: 5 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText(/^month$/i), { target: { value: '99' } })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('typing an out-of-range yearly "Day" does not commit it', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'yearlyOnMonthDay', month: 6, dayOfMonth: 5 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText(/^day$/i), { target: { value: '99' } })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('renders the first-due date entry for a completion-anchored rule', () => {
    render(
      <RecurrenceEditor
        value={{ anchor: 'completion', kind: 'everyNDays', n: 2 }}
        onChange={() => {}}
        firstDue="2026-09-10"
        onFirstDueChange={() => {}}
      />,
    )

    expect(screen.getByLabelText(/first due/i)).toHaveValue('2026-09-10')
  })

  it('changing kind away from a completion anchor clears the first-due date', () => {
    const onFirstDueChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'completion', kind: 'everyNDays', n: 2 }}
        onChange={() => {}}
        firstDue="2026-09-10"
        onFirstDueChange={onFirstDueChange}
      />,
    )

    fireEvent.change(screen.getByLabelText(/repeats/i), { target: { value: 'calendar:everyNDays' } })

    expect(onFirstDueChange).toHaveBeenCalledWith(null)
  })

  it('changing between completion-anchored kinds leaves the first-due date alone', () => {
    const onFirstDueChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'completion', kind: 'everyNDays', n: 2 }}
        onChange={() => {}}
        firstDue="2026-09-10"
        onFirstDueChange={onFirstDueChange}
      />,
    )

    fireEvent.change(screen.getByLabelText(/repeats/i), { target: { value: 'completion:everyNWeeks' } })

    expect(onFirstDueChange).not.toHaveBeenCalled()
  })

  it('changing between calendar-anchored kinds leaves the first-due date alone', () => {
    const onFirstDueChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNDays', n: 2 }}
        onChange={() => {}}
        firstDue="2026-09-10"
        onFirstDueChange={onFirstDueChange}
      />,
    )

    fireEvent.change(screen.getByLabelText(/repeats/i), { target: { value: 'calendar:everyNWeeks' } })

    expect(onFirstDueChange).not.toHaveBeenCalled()
  })

  it('clearing an Every N field then typing commits the replacement, not a prefixed value', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNDays', n: 3 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    const field = screen.getByLabelText(/every n/i)
    fireEvent.change(field, { target: { value: '' } })
    fireEvent.change(field, { target: { value: '12' } })

    expect(field).toHaveValue(12)
    expect(onChange).toHaveBeenCalledWith({ anchor: 'calendar', kind: 'everyNDays', n: 12 })
    expect(onChange).not.toHaveBeenCalledWith({ anchor: 'calendar', kind: 'everyNDays', n: 312 })
  })

  // ADR-0006: switching kind changes which sub-fields render. The <select> must not be swapped
  // for a different node, and a sub-field's own change must not remount the <select> either —
  // every sub-field group mounts once and toggles via `hidden`, never a conditional-render branch.
  it('the kind select survives its own change', () => {
    render(<RecurrenceEditor value={null} onChange={() => {}} firstDue={null} onFirstDueChange={() => {}} />)

    const el = screen.getByLabelText(/repeats/i)
    fireEvent.change(el, { target: { value: 'calendar:everyNDays' } })

    expect(screen.getByLabelText(/repeats/i)).toBe(el)
  })

  // [hidden] alone is not enough to hide these groups — `[hidden] { display: none }` lives in the
  // UA stylesheet, and index.css's `.stack`/`.chipset` both declare an author-origin `display`,
  // which wins over the UA rule at equal specificity regardless of source order. jsdom does not
  // load index.css, so this only asserts the JS half (the `hidden` attribute itself is correct);
  // index.css's `[hidden] { display: none !important }` is the other half, and is E2E's to hold
  // (see TEST-INVENTORY.md).
  it('marks every non-matching sub-field group hidden for "does not repeat"', () => {
    const { container } = render(
      <RecurrenceEditor value={null} onChange={() => {}} firstDue={null} onFirstDueChange={() => {}} />,
    )

    for (const group of ['everyN', 'weekdays', 'dayOfMonth', 'monthDay', 'firstDue']) {
      expect(container.querySelector(`[data-group="${group}"]`)).toHaveAttribute('hidden')
    }
  })

  it('marks only the matching sub-field group visible for "every N weeks"', () => {
    const { container } = render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNWeeks', n: 1, weekdays: [] }}
        onChange={() => {}}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    expect(container.querySelector('[data-group="everyN"]')).not.toHaveAttribute('hidden')
    expect(container.querySelector('[data-group="weekdays"]')).not.toHaveAttribute('hidden')
    expect(container.querySelector('[data-group="dayOfMonth"]')).toHaveAttribute('hidden')
    expect(container.querySelector('[data-group="monthDay"]')).toHaveAttribute('hidden')
    expect(container.querySelector('[data-group="firstDue"]')).toHaveAttribute('hidden')
  })

  it('a sub-field survives a kind change without remounting the select', () => {
    const onChange = vi.fn()
    render(
      <RecurrenceEditor
        value={{ anchor: 'calendar', kind: 'everyNDays', n: 3 }}
        onChange={onChange}
        firstDue={null}
        onFirstDueChange={() => {}}
      />,
    )

    const select = screen.getByLabelText(/repeats/i)
    fireEvent.change(select, { target: { value: 'calendar:everyNWeeks' } })

    expect(screen.getByLabelText(/repeats/i)).toBe(select)
  })
})
