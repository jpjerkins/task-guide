import { render, screen, fireEvent } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { DateRail } from './DateRail'

it('the_rail_renders_the_fixed_10_day_span_around_today_21_buttons_ten_back_through_ten_forward_marks_the_selected_date_and_dots_only_the_dates_carrying_an_Override_or_an_Event', () => {
  const props = { from: '2026-10-22', to: '2026-11-11', selected: '2026-11-01', marked: ['2026-10-24', '2026-11-03'], onSelect: vi.fn() }
  const { container, rerender } = render(<DateRail {...props} />)
  const rail = container.querySelector('.weekstrip')
  expect(rail).not.toBeNull()
  expect(rail?.children).toHaveLength(21)
  expect(screen.getAllByRole('button')[0]).toHaveAccessibleName('2026-10-22')
  expect(screen.getAllByRole('button')[20]).toHaveAccessibleName('2026-11-11')
  expect(screen.getByRole('button', { name: '2026-11-01' })).toHaveAttribute('aria-current', 'date')
  expect(container.querySelectorAll('.weekstrip > button > .dot:not(.blank)')).toHaveLength(2)
  for (const date of props.marked) expect(screen.getByRole('button', { name: date }).querySelector('.dot')).not.toHaveClass('blank')
  expect(container.querySelectorAll('.weekstrip > button > .wd')).toHaveLength(21)
  expect(container.querySelectorAll('.weekstrip > button > .dn')).toHaveLength(21)
  fireEvent.click(screen.getByRole('button', { name: '2026-11-03' }))
  expect(props.onSelect).toHaveBeenCalledWith('2026-11-03')
  rerender(<DateRail {...props} selected="2027-12-25" />)
  expect(screen.getAllByRole('button')).toHaveLength(21)
  expect(container.querySelector('[aria-current]')).toBeNull()
})

// jsdom has no layout, so scrollIntoView is undefined and this asserts the call, not a
// resulting offset. `block: 'nearest'` matters: without it the browser also scrolls the page
// to the rail, which is #140's second bug.
it('the_rail_scrolls_the_selected_date_into_view_on_mount_and_whenever_the_selection_changes', () => {
  const scrollIntoView = vi.fn()
  Element.prototype.scrollIntoView = scrollIntoView
  const props = { from: '2026-10-22', to: '2026-11-11', selected: '2026-11-01', marked: [], onSelect: vi.fn() }
  const { rerender } = render(<DateRail {...props} />)
  const selectedButton = screen.getByRole('button', { name: '2026-11-01' })
  expect(scrollIntoView).toHaveBeenCalledWith(expect.objectContaining({ inline: 'center' }))
  expect(scrollIntoView.mock.instances[scrollIntoView.mock.instances.length - 1]).toBe(selectedButton)
  scrollIntoView.mockClear()
  rerender(<DateRail {...props} selected="2026-11-03" />)
  const newSelectedButton = screen.getByRole('button', { name: '2026-11-03' })
  expect(scrollIntoView).toHaveBeenCalledWith(expect.objectContaining({ inline: 'center' }))
  expect(scrollIntoView.mock.instances[scrollIntoView.mock.instances.length - 1]).toBe(newSelectedButton)
})
