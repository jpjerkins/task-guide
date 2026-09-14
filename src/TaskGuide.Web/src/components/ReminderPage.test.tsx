import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
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
