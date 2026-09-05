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
