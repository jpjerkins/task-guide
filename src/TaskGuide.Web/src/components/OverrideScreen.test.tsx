import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { components } from '../api/schema'
import { OverrideScreen } from './OverrideScreen'

type Day = components['schemas']['DayShape']
const window = { id: { value: 'w_original' }, name: 'Family time', start: '18:00:00', end: '19:00:00', tags: { dimensions: {}, looseTags: [] } }
const template = { id: 'dt_christmas', name: 'Christmas', windows: [window], eventPrototypes: [] }
const days = new Map<string, Day>()
function json(body: unknown) { return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } }) }
let fetch: ReturnType<typeof vi.fn<(url: string, init?: RequestInit) => Promise<Response>>>
async function read(url: string): Promise<Response> {
  if (url.startsWith('/api/days/')) {
    const date = url.slice('/api/days/'.length)
    return json(days.get(date) ?? { date, windows: [window], events: [], isOverridden: false })
  }
  if (url.includes('clobber-check')) return json([])
  if (url === '/api/day-templates') return json([template])
  return new Response(null, { status: 404 })
}
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-11-01T18:00:00Z'))
  days.clear()
  fetch = vi.fn(read)
  vi.stubGlobal('fetch', fetch)
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers() })

it('Any_date_opens_the_shared_DateEntry_and_selecting_a_date_beyond_the_rail_shows_that_date_still_without_growing_the_rail', async () => {
  const { container } = render(<OverrideScreen />)
  await screen.findByText('Family time')
  expect(container.querySelector('.scope.one > .g')).toHaveTextContent('◈')
  expect(container.querySelector('.scope.one')).toHaveTextContent('The first change copies the day off its shape')
  const rail = container.querySelector('.weekstrip')
  expect(rail?.children).toHaveLength(21)
  const secH = container.querySelector('.sec-h.with-tog') as HTMLElement
  expect(secH).toHaveTextContent('Ten days either side of today')
  const toggle = within(secH).getByRole('button', { name: 'Any date…' })
  expect(toggle).toHaveAttribute('aria-pressed', 'false')
  expect(toggle).toHaveAttribute('aria-expanded', 'false')
  fireEvent.click(toggle)
  expect(toggle).toHaveAttribute('aria-pressed', 'true')
  const input = screen.getByLabelText('Any date')
  fireEvent.change(input, { target: { value: '2027-12-25' } })
  await screen.findByRole('heading', { name: 'Sat 25 Dec' })
  await waitFor(() => expect(container.querySelector('.scope.one')).toHaveTextContent('Sat 25 Dec only'))
  expect(container.querySelector('.sec-h.with-tog')).toHaveTextContent('Sat 25 Dec · beyond the rail')
  expect(screen.getByLabelText('Any date')).toBe(input)
  expect(rail?.children).toHaveLength(21)
  expect(rail?.querySelector('[aria-current]')).toBeNull()
  expect(container.querySelector('[style]')).toBeNull()
  fireEvent.click(toggle)
  expect(toggle).toHaveAttribute('aria-pressed', 'false')
  expect(screen.queryByLabelText('Any date')).not.toBeInTheDocument()
})

it('the_nav_title_is_the_short_date_and_the_sub_falls_back_to_pattern_or_override_when_no_write_supplied_a_label', async () => {
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  expect(screen.getByRole('heading', { name: 'Sun 1 Nov' })).toBeInTheDocument()
  expect(document.querySelector('.sub')).toHaveTextContent('Following the pattern')
})

it('the_sub_reads_One-off_day_for_an_override_with_no_saved_label_and_the_saved_label_once_a_write_supplies_one', async () => {
  days.set('2026-11-01', { date: '2026-11-01', windows: [window], events: [], isOverridden: true })
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  expect(document.querySelector('.sub')).toHaveTextContent('One-off day')
})

it('selecting_a_rail_date_changes_the_shown_date_without_adding_a_rail_entry_the_rail_never_grows', async () => {
  days.set('2026-11-03', { date: '2026-11-03', windows: [], events: [], isOverridden: true })
  const { container } = render(<OverrideScreen />)
  await waitFor(() => expect(screen.getByRole('button', { name: '2026-11-03' }).querySelector('.dot')).not.toHaveClass('blank'))
  fireEvent.click(screen.getByRole('button', { name: '2026-11-03' }))
  await waitFor(() => expect(container.querySelector('.scope.one')).toHaveTextContent('already an override'))
  expect(container.querySelector('.weekstrip')?.children).toHaveLength(21)
  expect(screen.getByRole('button', { name: '2026-11-03' })).toHaveAttribute('aria-current', 'date')
})

it('stamping_a_Day_template_onto_a_date_renders_the_copies_the_windows_in_note_and_sends_the_stamp_for_that_date_alone', async () => {
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/overrides' && init?.method === 'POST') {
      days.set('2026-11-01', { date: '2026-11-01', windows: [window], events: [], isOverridden: true })
      return json([{ date: '2026-11-01', windows: [window], used: { templateId: template.id, templateName: template.name } }])
    }
    return read(url)
  })
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Stamp a whole shape onto this date…' }))
  const picker = await screen.findByRole('dialog', { name: 'Sun 1 Nov' })
  expect(picker).toHaveTextContent('It copies the windows in')
  expect(picker.querySelector('.sheet > .grabber')).not.toBeNull()
  const shape = await within(picker).findByRole('button', { name: /Christmas/ })
  expect(shape).toHaveClass('pickrow')
  expect(shape.querySelector('.who > .nm')).toHaveTextContent('Christmas')
  expect(shape.querySelector('.who > .sub2')).toHaveTextContent('1 window')
  fireEvent.click(shape)
  await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/overrides', expect.objectContaining({ method: 'POST', body: JSON.stringify({ from: '2026-11-01', to: '2026-11-01', templateId: template.id }) })))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  await waitFor(() => expect(screen.getByText(/already an override/)).toHaveTextContent('Christmas'))
})

it('stamping_onto_a_date_that_already_carries_an_Override_confirms_the_clobber_before_writing', async () => {
  days.set('2026-11-01', { date: '2026-11-01', windows: [window], events: [], isOverridden: true })
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('clobber-check')) return json(['2026-11-01'])
    if (url === '/api/overrides' && init?.method === 'POST') return json([{ date: '2026-11-01', windows: [window], used: null }])
    return read(url)
  })
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Stamp a whole shape onto this date…' }))
  fireEvent.click(await screen.findByRole('button', { name: /Christmas/ }))
  const confirmation = await screen.findByRole('dialog', { name: 'Replace 1 Override?' })
  expect(confirmation).toHaveTextContent('Sun 1 Nov')
  expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  fireEvent.click(within(confirmation).getByRole('button', { name: 'Replace it' }))
  await waitFor(() => expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1))
})

it('a_range_landing_on_dates_that_already_carry_an_Override_names_every_one_of_them_in_a_single_confirmation_before_the_write_not_one_prompt_per_date', async () => {
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('clobber-check')) return json(['2026-12-24', '2026-12-26', '2026-12-28'])
    if (init?.method === 'POST') return json([])
    return read(url)
  })
  const { container } = render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Stamp a whole shape onto this date…' }))
  const picker = await screen.findByRole('dialog')
  fireEvent.click(within(picker).getByRole('button', { name: 'A range…' }))
  const from = within(picker).getByLabelText('From')
  const to = within(picker).getByLabelText('To')
  fireEvent.change(from, { target: { value: '2026-12-24' } })
  fireEvent.change(to, { target: { value: '2026-12-28' } })
  expect(within(picker).getByLabelText('From')).toBe(from)
  expect(within(picker).getByLabelText('To')).toBe(to)
  fireEvent.click(await within(picker).findByRole('button', { name: /Christmas/ }))
  const confirmation = await screen.findByRole('dialog', { name: 'Replace 3 Overrides?' })
  expect(Array.from(confirmation.querySelectorAll('.damage > .row .title'), node => node.textContent)).toEqual(['Thu 24 Dec', 'Sat 26 Dec', 'Mon 28 Dec'])
  expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  fireEvent.click(within(confirmation).getByRole('button', { name: 'Replace 3 and stamp all 5' }))
  await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/overrides', expect.objectContaining({ method: 'POST', body: JSON.stringify({ from: '2026-12-24', to: '2026-12-28', templateId: template.id }) })))
  expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
  expect(container.querySelector('.weekstrip')?.children).toHaveLength(21)
})

it('window_and_event_rows_show_hm_times_dimension_and_loose_tag_pills_and_a_window_row_carries_an_inert_chev', async () => {
  const tagged = { id: { value: 'w_tagged' }, name: 'Tagged', start: '07:30:00', end: '09:00:00', tags: { dimensions: { Location: [{ value: 'Home' }, { value: 'Office' }] }, looseTags: [{ value: 'urgent' }] } }
  days.set('2026-11-01', { date: '2026-11-01', windows: [tagged], events: [{ id: { value: 'ev_dinner' }, date: '2026-11-01', name: 'Dinner out', start: '18:00:00', end: '19:00:00', tags: { dimensions: {}, looseTags: [] }, absenceNotice: null }], isOverridden: false })
  const { container } = render(<OverrideScreen />)
  await screen.findByText('Tagged')
  const row = screen.getByText('Tagged').closest('.row') as HTMLElement
  expect(row.querySelector('.pill.dur')).toHaveTextContent('7:30a–9a')
  expect(row.querySelector('.pill.dim')).toHaveTextContent('location: Home / Office')
  expect(row.querySelector('.pill.inert')).toHaveTextContent('urgent · inert')
  expect(row.querySelector('.chev')).toHaveTextContent('›')
  expect(container.querySelectorAll('.pill.dim')).not.toHaveLength(0)
  expect(container.textContent).not.toMatch(/\bfit\b/)
  const eventRow = screen.getByText('Dinner out').closest('.row') as HTMLElement
  expect(eventRow.querySelector('.pill.dur')).toHaveTextContent('6p–7p')
  expect(eventRow.querySelector('.chev')).toBeNull()
})

it('reverting_a_date_removes_its_Override_and_the_date_reads_as_following_the_pattern_again', async () => {
  days.set('2026-11-01', { date: '2026-11-01', windows: [], events: [], isOverridden: true })
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'DELETE') { days.delete('2026-11-01'); return new Response(null, { status: 204 }) }
    return read(url)
  })
  const { container } = render(<OverrideScreen />)
  fireEvent.click(await screen.findByRole('button', { name: 'Put it back on the pattern' }))
  await screen.findByText('Family time')
  expect(fetch).toHaveBeenCalledWith('/api/overrides/2026-11-01', expect.objectContaining({ method: 'DELETE', body: undefined }))
  expect(container.querySelector('.scope.one')).toHaveTextContent('The first change copies the day off its shape')
  expect(screen.queryByRole('button', { name: 'Put it back on the pattern' })).not.toBeInTheDocument()
})

it('promotion_names_the_new_shape_lists_the_windows_it_will_carry_and_states_that_the_source_date_keeps_its_own_copy_after_it_the_source_date_still_reads_as_its_own_shape_and_not_as_a_link', async () => {
  days.set('2026-11-01', { date: '2026-11-01', windows: [window], events: [], isOverridden: true })
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.endsWith('/promote') && init?.method === 'POST') return json({ ...template, name: 'Family Sunday' })
    return read(url)
  })
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Save as a shape' }))
  const sheet = screen.getByRole('dialog', { name: 'Save this day as a shape' })
  expect(sheet.querySelector('.sheet > .list > .row > .body > .title')).toHaveTextContent('Family time')
  expect(sheet).toHaveTextContent('does not re-link')
  expect(sheet).toHaveTextContent('keeps its own copy')
  expect(within(sheet).getByLabelText('Call it')).toHaveValue('One-off day v2')
  fireEvent.change(within(sheet).getByLabelText('Call it'), { target: { value: 'Family Sunday' } })
  fireEvent.click(within(sheet).getByRole('button', { name: 'Save the shape' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(fetch).toHaveBeenCalledWith('/api/overrides/2026-11-01/promote', expect.objectContaining({ method: 'POST', body: JSON.stringify({ name: 'Family Sunday' }) }))
  await screen.findByRole('button', { name: 'Put it back on the pattern' })
  expect(screen.getByText('Family time')).toBeInTheDocument()
})

it('the_promote_sheets_window_list_carries_dimension_and_loose_tag_pills', async () => {
  const tagged = { id: { value: 'w_tagged' }, name: 'Tagged', start: '07:30:00', end: '09:00:00', tags: { dimensions: { Location: [{ value: 'Home' }] }, looseTags: [{ value: 'urgent' }] } }
  days.set('2026-11-01', { date: '2026-11-01', windows: [tagged], events: [], isOverridden: false })
  render(<OverrideScreen />)
  await screen.findByText('Tagged')
  fireEvent.click(screen.getByRole('button', { name: 'Save as a shape' }))
  const sheet = screen.getByRole('dialog', { name: 'Save this day as a shape' })
  const row = within(sheet).getByText('Tagged').closest('.row') as HTMLElement
  expect(row.querySelector('.pill.dim')).toHaveTextContent('location: Home')
  expect(row.querySelector('.pill.inert')).toHaveTextContent('urgent · inert')
})

it('cancelling_replacement_keeps_the_stamp_sheet_range_scope_open_and_writes_nothing', async () => {
  fetch.mockImplementation(async url => url.includes('clobber-check') ? json(['2026-11-01']) : read(url))
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Stamp a whole shape onto this date…' }))
  const picker = await screen.findByRole('dialog')
  fireEvent.click(within(picker).getByRole('button', { name: 'A range…' }))
  fireEvent.click(within(picker).getByRole('button', { name: /Christmas/ }))
  const confirmation = await screen.findByRole('dialog', { name: 'Replace 1 Override?' })
  fireEvent.click(within(confirmation).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Replace 1 Override?' })).not.toBeInTheDocument())
  expect(within(picker).getByLabelText('From')).toHaveValue('2026-11-01')
  expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
})

it('a_failed_stamp_keeps_the_picker_open_and_reports_the_error_inside_it', async () => {
  fetch.mockImplementation(async (url, init) => init?.method === 'POST' ? new Response(null, { status: 409 }) : read(url))
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: 'Stamp a whole shape onto this date…' }))
  fireEvent.click(await screen.findByRole('button', { name: /Christmas/ }))
  const picker = screen.getByRole('dialog', { name: 'Sun 1 Nov' })
  expect(await within(picker).findByRole('alert')).toHaveTextContent('409')
  expect(within(picker).getByRole('button', { name: /Christmas/ })).toBeEnabled()
})

it('an_Event_marks_its_rail_date_and_the_selected_date_lists_the_resolved_Event', async () => {
  days.set('2026-11-01', { date: '2026-11-01', windows: [], isOverridden: false, events: [{ id: { value: 'ev_dinner' }, date: '2026-11-01', name: 'Dinner out', start: '18:00:00', end: '19:00:00', tags: { dimensions: {}, looseTags: [] }, absenceNotice: null }] })
  render(<OverrideScreen />)
  await screen.findByText('Dinner out')
  await waitFor(() => expect(screen.getByRole('button', { name: '2026-11-01' }).querySelector('.dot')).not.toHaveClass('blank'))
  expect(screen.getByText('No windows — a completely blank day.')).toBeInTheDocument()
})

it('the_date_view_opens_the_existing_Event_editor_for_the_selected_date', async () => {
  render(<OverrideScreen />)
  await screen.findByText('Family time')
  fireEvent.click(screen.getByRole('button', { name: '＋ Event' }))
  expect(screen.getByRole('heading', { name: 'New event' })).toBeInTheDocument()
  expect(screen.getByLabelText('Name')).toHaveValue('')
})
