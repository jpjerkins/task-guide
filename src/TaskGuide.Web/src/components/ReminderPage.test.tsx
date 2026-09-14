import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { components } from '../api/schema'
import { ReminderPage } from './ReminderPage'

type ReminderPageResponse = components['schemas']['ReminderPageResponse']
type DimensionResponse = components['schemas']['DimensionResponse']

const DATE = '2026-09-13'
const WINDOW_ID = 'w_evening'

function page(overrides: Partial<ReminderPageResponse> = {}): ReminderPageResponse {
  return {
    date: DATE,
    windowName: 'Evening wind-down',
    windowStart: '19:00:00',
    windowEnd: '21:30:00',
    snooze: null,
    fallbackEventName: null,
    firedAs: 'window',
    matches: [],
    isLive: true,
    staleLine: null,
    matchingOn: { declared: {}, defaulted: {} },
    footer: { toProcess: 0, stale: 0, orphans: 0 },
    failedFetches: [],
    ...overrides,
  }
}

function json(body: unknown, status = 200) {
  return new Response(status === 204 ? null : JSON.stringify(body), { status })
}

let currentPage: ReminderPageResponse
let dimensions: DimensionResponse[]
let unprocessed: unknown[]
let fetch: ReturnType<typeof vi.fn<(url: string, init?: RequestInit) => Promise<Response>>>

async function read(url: string): Promise<Response> {
  if (url === `/api/reminders/${DATE}/${WINDOW_ID}`) return json(currentPage)
  if (url === '/api/dimensions') return json(dimensions)
  if (url === '/api/tasks?status=unprocessed') return json(unprocessed)
  return new Response(null, { status: 404 })
}

beforeEach(() => {
  currentPage = page()
  dimensions = []
  unprocessed = []
  fetch = vi.fn(read)
  vi.stubGlobal('fetch', fetch)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

it('the_page_renders_the_Windows_own_name_span_and_date', async () => {
  const { unmount } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Evening wind-down')
  expect(screen.getByText('7p–9:30p · 2026-09-13')).toBeInTheDocument()
  unmount()

  currentPage = page({
    windowName: null,
    windowStart: null,
    windowEnd: null,
    fallbackEventName: 'Grocery run',
    firedAs: 'fallback',
  })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Grocery run')
})

it('all_matches_render_not_the_pushs_shortlist_of_three', async () => {
  currentPage = page({
    matches: [
      { id: 't1', title: 'Water plants', duration: '10', createdAt: '2026-09-01T00:00:00Z' },
      { id: 't2', title: 'Fold laundry', duration: '30', createdAt: '2026-09-01T00:00:00Z' },
      { id: 't3', title: 'Read a chapter', duration: '30', createdAt: '2026-09-01T00:00:00Z' },
      { id: 't4', title: 'Stretch', duration: '10', createdAt: '2026-09-01T00:00:00Z' },
    ],
  })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Water plants')
  expect(screen.getByText('Fold laundry')).toBeInTheDocument()
  expect(screen.getByText('Read a chapter')).toBeInTheDocument()
  expect(screen.getByText('Stretch')).toBeInTheDocument()
})

it('a_Window_that_matched_nothing_renders_Nothing_fits_split_on_whether_a_Reminder_fired', async () => {
  currentPage = page({ matches: [], firedAs: null })
  const { container, unmount } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText(/Nothing fits/)
  expect(container.querySelector('.empty')).toHaveTextContent('Nothing fits.No notification would have fired.')
  unmount()

  currentPage = page({ matches: [], firedAs: 'unconditional' })
  const { container: container2 } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText(/Nothing fits/)
  expect(container2.querySelector('.empty')).toHaveTextContent('Nothing fits.')
  expect(container2.querySelector('.empty')).not.toHaveTextContent('No notification would have fired.')
})

it('an_Unprocessed_Task_in_the_footer_count_is_repairable_inline', async () => {
  currentPage = page({ footer: { toProcess: 1, stale: 0, orphans: 0 } })
  unprocessed = [{ id: 'tu1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('File the receipt')
  expect(screen.getByRole('button', { name: '2m' })).toBeDisabled()
  expect(screen.getByRole('button', { name: '10m' })).toBeDisabled()
  expect(screen.getByRole('button', { name: '30m' })).toBeDisabled()
  expect(screen.getByRole('button', { name: '60m' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Longer' })).toBeDisabled()
})

it('the_Snooze_control_names_the_interval_the_server_gave_it', async () => {
  currentPage = page({ snooze: { intervalMinutes: 17, suppression: null } })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  expect(await screen.findByRole('button', { name: 'Snooze 17 min' })).toBeInTheDocument()
})

it('when_the_server_reports_Snooze_unavailable_the_control_is_suppressed_not_hidden', async () => {
  currentPage = page({ snooze: { intervalMinutes: 17, suppression: 'Snooze ends at midnight' } })
  const { unmount } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Snooze ends at midnight')
  expect(screen.queryByRole('button', { name: /Snooze/ })).not.toBeInTheDocument()
  unmount()

  currentPage = page({ snooze: { intervalMinutes: 17, suppression: 'This reminder was for yesterday' } })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('This reminder was for yesterday')
  expect(screen.queryByRole('button', { name: /Snooze/ })).not.toBeInTheDocument()
})

it('a_rejected_Snooze_POST_renders_the_same_line_the_disabled_state_would_have_shown', async () => {
  currentPage = page({ snooze: { intervalMinutes: 17, suppression: null } })
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' && url.endsWith('/snooze')) {
      currentPage = page({ snooze: { intervalMinutes: 17, suppression: 'Snooze ends at midnight' } })
      return new Response(JSON.stringify({ error: 'Snooze ends at midnight' }), { status: 409 })
    }
    return read(url)
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await user.click(await screen.findByRole('button', { name: 'Snooze 17 min' }))
  await screen.findByText('Snooze ends at midnight')
  expect(screen.queryByRole('button', { name: /Snooze/ })).not.toBeInTheDocument()
})

it('Matching_on_is_gated_by_the_same_page_level_predicate_and_carries_its_own_suppression_line', async () => {
  currentPage = page({
    isLive: false,
    staleLine: 'This reminder was for yesterday',
    matchingOn: { declared: { weather: ['sunny'] }, defaulted: {} },
  })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('This reminder was for yesterday')
  expect(screen.queryByText('Matching on')).not.toBeInTheDocument()
})

it('mark_off_and_Postpone_stay_live_on_a_page_past_its_Day_boundary', async () => {
  currentPage = page({
    isLive: false,
    staleLine: 'This reminder was for yesterday',
    matches: [{ id: 't1', title: 'Water plants', duration: '10', createdAt: '2026-09-01T00:00:00Z' }],
  })
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' && url === '/api/tasks/t1/completions') return new Response(null, { status: 204 })
    if (init?.method === 'PUT' && url === '/api/tasks/t1/postpone') return new Response(null, { status: 204 })
    return read(url)
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Water plants')

  await user.click(screen.getByRole('button', { name: 'Mark Water plants done' }))
  await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/tasks/t1/completions', expect.objectContaining({ method: 'POST' })))
  await waitFor(() => expect(fetch).toHaveBeenCalledWith(`/api/reminders/${DATE}/${WINDOW_ID}`))

  await user.click(screen.getByRole('button', { name: 'Not now' }))
  const input = screen.getByLabelText('Not now')
  fireEvent.change(input, { target: { value: '2026-09-20' } })
  await user.click(screen.getByRole('button', { name: 'Postpone' }))
  await waitFor(() =>
    expect(fetch).toHaveBeenCalledWith(
      '/api/tasks/t1/postpone',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ date: '2026-09-20' }) }),
    ),
  )
})

it('Postpone_sends_nothing_until_its_date_is_confirmed', async () => {
  currentPage = page({
    matches: [{ id: 't1', title: 'Water plants', duration: '10', createdAt: '2026-09-01T00:00:00Z' }],
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Water plants')

  await user.click(screen.getByRole('button', { name: 'Not now' }))
  const input = screen.getByLabelText('Not now')
  expect(screen.getByRole('button', { name: 'Postpone' })).toBeDisabled()

  // A half-typed year (e.g. "0002" mid-keystroke) is a valid-looking date value the input
  // fires onChange for; it must not write until the Postpone button is pressed.
  fireEvent.change(input, { target: { value: '0002-09-20' } })
  expect(fetch).not.toHaveBeenCalledWith('/api/tasks/t1/postpone', expect.anything())
  expect(screen.getByLabelText('Not now')).toBe(input)

  fireEvent.change(input, { target: { value: '2026-09-20' } })
  expect(fetch).not.toHaveBeenCalledWith('/api/tasks/t1/postpone', expect.anything())
  expect(screen.getByRole('button', { name: 'Postpone' })).not.toBeDisabled()

  await user.click(screen.getByRole('button', { name: 'Postpone' }))
  await waitFor(() =>
    expect(fetch).toHaveBeenCalledWith(
      '/api/tasks/t1/postpone',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ date: '2026-09-20' }) }),
    ),
  )
})

it('the_footer_renders_the_counts_as_a_partition_plus_a_disjoint_orphan_count', async () => {
  currentPage = page({ footer: { toProcess: 6, stale: 3, orphans: 2 } })
  const { container, unmount } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await waitFor(() => expect(container.querySelector('.footer-count')).toHaveTextContent('6 to process · 3 stale · 2 orphans'))
  unmount()

  currentPage = page({ footer: { toProcess: 0, stale: 0, orphans: 0 } })
  const { container: container2 } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Evening wind-down')
  expect(container2.querySelector('.footer-count')).not.toBeInTheDocument()
})

it('a_failed_fetched_Dimension_check_renders_its_footer_note_beside_the_counts_named_generically_off_the_response', async () => {
  currentPage = page({ failedFetches: ['weather'] })
  const { unmount } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Weather unavailable')
  unmount()

  currentPage = page({ failedFetches: ['tide'] })
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Tide unavailable')
})

it('Matching_on_renders_the_declared_axes_distinctly_from_the_defaulted_ones', async () => {
  currentPage = page({
    matchingOn: { declared: { weather: ['sunny'] }, defaulted: { energy: ['high'] } },
  })
  const { container } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Evening wind-down')
  const declaredPill = Array.from(container.querySelectorAll('.adjust-sum .pill')).find((el) =>
    el.textContent?.includes('weather'),
  )
  const defaultedPill = Array.from(container.querySelectorAll('.adjust-sum .pill')).find((el) =>
    el.textContent?.includes('energy'),
  )
  expect(declaredPill).toHaveClass('pill', 'now')
  expect(defaultedPill).toHaveClass('pill', 'dim')
})

it('toggling_a_Matching_on_chip_re_reads_the_match_list_from_the_server_the_rendered_list_is_never_filtered_client_side', async () => {
  currentPage = page({
    matchingOn: { declared: { weather: ['sunny'] }, defaulted: {} },
    matches: [{ id: 't1', title: 'Water plants', duration: '10', createdAt: '2026-09-01T00:00:00Z' }],
  })
  dimensions = [
    { id: 'weather', label: 'Weather', algebra: 'categorical', values: ['sunny', 'rainy'], taskDefault: null, windowDefault: null, source: 'authored' },
  ]
  let putBody: unknown = null
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT' && url === '/api/right-now/matching-on') {
      putBody = init.body
      currentPage = page({
        matchingOn: { declared: { weather: ['sunny', 'rainy'] }, defaulted: {} },
        matches: [
          { id: 't1', title: 'Water plants', duration: '10', createdAt: '2026-09-01T00:00:00Z' },
          { id: 't2', title: 'Walk in the rain', duration: '30', createdAt: '2026-09-01T00:00:00Z' },
        ],
      })
      return new Response(null, { status: 204 })
    }
    return read(url)
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Water plants')
  await user.click(screen.getByRole('button', { name: 'rainy' }))
  await screen.findByText('Walk in the rain')
  expect(screen.getByText('Water plants')).toBeInTheDocument()
  expect(putBody).toBe(JSON.stringify({ date: DATE, windowId: WINDOW_ID, dimensions: { weather: ['sunny', 'rainy'] } }))
})

it('the_Matching_on_chipset_survives_its_own_press_same_DOM_nodes_before_and_after', async () => {
  currentPage = page({
    matchingOn: { declared: { weather: ['sunny'] }, defaulted: {} },
  })
  dimensions = [
    { id: 'weather', label: 'Weather', algebra: 'categorical', values: ['sunny', 'rainy'], taskDefault: null, windowDefault: null, source: 'authored' },
  ]
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT' && url === '/api/right-now/matching-on') {
      currentPage = page({ matchingOn: { declared: { weather: ['sunny', 'rainy'] }, defaulted: {} } })
      return new Response(null, { status: 204 })
    }
    return read(url)
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  const chip = await screen.findByRole('button', { name: 'rainy' })
  await user.click(chip)
  await waitFor(() => expect(chip).toHaveAttribute('aria-pressed', 'true'))
  expect(screen.getByRole('button', { name: 'rainy' })).toBe(chip)
  expect(chip.isConnected).toBe(true)
})

it('a_fallback_pushs_landing_page_has_no_Snooze_control_at_all_rather_than_a_disabled_one', async () => {
  currentPage = page({ windowName: null, fallbackEventName: 'Grocery run', firedAs: 'fallback', snooze: null })
  const { container } = render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await screen.findByText('Grocery run')
  expect(screen.queryByRole('button', { name: /Snooze/ })).not.toBeInTheDocument()
  expect(container.querySelector('.btn-row')).not.toBeInTheDocument()
})

it('an_adjustment_reports_back_that_the_date_is_now_an_Override', async () => {
  currentPage = page({ matchingOn: { declared: { weather: ['sunny'] }, defaulted: {} } })
  dimensions = [
    { id: 'weather', label: 'Weather', algebra: 'categorical', values: ['sunny', 'rainy'], taskDefault: null, windowDefault: null, source: 'authored' },
  ]
  fetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT' && url === '/api/right-now/matching-on') {
      currentPage = page({ matchingOn: { declared: { weather: ['sunny', 'rainy'] }, defaulted: {} } })
      return new Response(null, { status: 204 })
    }
    return read(url)
  })
  const user = userEvent.setup()
  render(<ReminderPage date={DATE} windowId={WINDOW_ID} />)
  await user.click(await screen.findByRole('button', { name: 'rainy' }))
  await screen.findByText(`${DATE} is now an override`)
})
