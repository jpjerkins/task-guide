import { createElement } from 'react'
import { act, renderHook, render, screen, fireEvent } from '@testing-library/react'
import { expect, it } from 'vitest'
import { DateEntry } from './shared/DateEntry'
import { useOverrideDateSelection } from './OverrideDateSelection'

it('picking_a_date_beyond_the_rail_changes_the_selection_without_growing_or_recentering_its_fixed_Chicago_span', () => {
  // UTC is November 2; Chicago is still November 1, the DST fall-back day.
  const { result } = renderHook(() => useOverrideDateSelection(new Date('2026-11-02T04:30:00Z')))
  expect(result.current.selectedDate).toBe('2026-11-01')
  expect(result.current.railSpan).toEqual({ from: '2026-10-22', to: '2026-11-11' })
  const railSpan = result.current.railSpan
  act(() => result.current.dateEntryProps.onChange('2027-12-25'))
  expect(result.current.selectedDate).toBe('2027-12-25')
  expect(result.current.dateEntryProps.value).toBe('2027-12-25')
  expect(result.current.railSpan).toBe(railSpan)
})

it('the_rails_date_control_survives_its_own_input_event_same_DOM_node_before_and_after', () => {
  function Escape() {
    const { dateEntryProps } = useOverrideDateSelection(new Date('2026-11-02T04:30:00Z'))
    return createElement(DateEntry, dateEntryProps)
  }
  render(createElement(Escape))
  const input = screen.getByLabelText('Pick a date…')
  fireEvent.change(input, { target: { value: '2027-12-25' } })
  expect(screen.getByLabelText('Pick a date…')).toBe(input)
  expect(input).toHaveValue('2027-12-25')
  fireEvent.change(input, { target: { value: '' } })
  expect(screen.getByLabelText('Pick a date…')).toBe(input)
  expect(input).toHaveValue('')
})

it('clearing_the_escape_keeps_the_shown_date_and_fixed_rail_span', () => {
  const { result } = renderHook(() => useOverrideDateSelection(new Date('2026-11-02T04:30:00Z')))
  act(() => result.current.dateEntryProps.onChange('2027-12-25'))
  act(() => result.current.dateEntryProps.onChange(null))
  expect(result.current.dateEntryProps.value).toBeNull()
  expect(result.current.selectedDate).toBe('2027-12-25')
  expect(result.current.railSpan).toEqual({ from: '2026-10-22', to: '2026-11-11' })
})
