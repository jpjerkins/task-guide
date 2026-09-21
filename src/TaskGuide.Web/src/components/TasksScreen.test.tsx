import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PushContext, type PushedScreen } from './shared/screenRegistry'
import { TaskDetail } from './TaskDetail'
import { TasksScreen } from './TasksScreen'

// Stubbed at the network boundary via a global `fetch` mock rather than MSW: the walking
// skeleton talks to exactly two endpoints (GET/POST /api/tasks), so a per-test fetch stub
// is less machinery than standing up MSW handlers for a surface this small.
function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
}

// A full raw wire TaskResponse, with every #163 field defaulted so a fixture need only name
// the fields a given test cares about.
function rawTask(overrides: Record<string, unknown> = {}) {
  return {
    id: '1',
    title: 'Water the plants',
    notes: null,
    duration: null,
    dimensions: {},
    looseTags: [],
    createdAt: '2026-09-01T00:00:00Z',
    status: 'active',
    eligible: true,
    deadline: null,
    defer: null,
    postpone: null,
    recurring: false,
    derived: false,
    opportunities: null,
    patternWeekCount: null,
    zeroKind: null,
    ...overrides,
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('TasksScreen', () => {
  it("renders a task's title and duration from a stubbed GET /api/tasks", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([rawTask({ id: '1', title: 'Water the plants', duration: 10 })]),
      ),
    )

    render(<TasksScreen />)

    expect(await screen.findByText('Water the plants')).toBeInTheDocument()
    expect(screen.getByText('10m')).toBeInTheDocument()
  })

  it('renders a task with a null duration and no duration pill', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([rawTask({ id: '1', title: 'Water the plants', duration: null, status: 'unprocessed' })]),
      ),
    )

    const { container } = render(<TasksScreen />)

    await userEvent.setup().click(await screen.findByRole('button', { name: /^Unprocessed /i }))
    expect(await screen.findByText('Water the plants')).toBeInTheDocument()
    expect(container.querySelector('.pill.dur')).not.toBeInTheDocument()
  })

  it('renders a numeric duration bucket as a minutes label', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([rawTask({ id: '1', title: 'Water the plants', duration: '30' })]),
      ),
    )

    render(<TasksScreen />)

    expect(await screen.findByText('Water the plants')).toBeInTheDocument()
    expect(screen.getByText('30m')).toBeInTheDocument()
  })

  it('renders a longer-bucket task with a "Longer" pill rather than a minutes label', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([rawTask({ id: '1', title: 'Sort out the loft', duration: 'longer' })]),
      ),
    )

    render(<TasksScreen />)

    expect(await screen.findByText('Sort out the loft')).toBeInTheDocument()
    expect(screen.getByText('Longer')).toBeInTheDocument()
    expect(screen.queryByText(/longerm/)).not.toBeInTheDocument()
  })

  it('renders the empty state when the API returns no tasks', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))

    render(<TasksScreen />)

    expect(await screen.findByText(/nothing here/i)).toBeInTheDocument()
  })

  it('renders an error state when the fetch rejects', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    render(<TasksScreen />)

    expect(await screen.findByText(/couldn.t load/i)).toBeInTheDocument()
  })

  it('renders an error state when the fetch returns a non-OK status', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 500 })),
    )

    render(<TasksScreen />)

    expect(await screen.findByText(/couldn.t load/i)).toBeInTheDocument()
  })

  it('quick-add: tapping a duration chip POSTs the entered title and duration, then re-fetches', async () => {
    const user = userEvent.setup()
    const fetchMock = vi
      .fn()
      // initial GET on mount
      .mockResolvedValueOnce(jsonResponse([]))
      // POST from quick-add
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      // re-fetch after POST
      .mockResolvedValueOnce(
        jsonResponse([rawTask({ id: '2', title: 'Call the vet', duration: 30 })]),
      )
    vi.stubGlobal('fetch', fetchMock)

    render(<TasksScreen />)
    await screen.findByText(/nothing here/i)

    await user.click(screen.getByLabelText(/quick add a task/i))
    await user.type(screen.getByPlaceholderText(/what is it/i), 'Call the vet')
    // The duration chip IS the submit — no separate confirm button.
    await user.click(screen.getByRole('button', { name: '30m' }))

    await screen.findByText('Call the vet')

    const [, postCall] = fetchMock.mock.calls
    expect(postCall[0]).toBe('/api/tasks')
    expect(postCall[1]?.method).toBe('POST')
    expect(JSON.parse(postCall[1]?.body as string)).toEqual({
      title: 'Call the vet',
      duration: 30,
    })
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('quick-add: a duration chip does not submit while the title is empty', async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    render(<TasksScreen />)
    await screen.findByText(/nothing here/i)

    await user.click(screen.getByLabelText(/quick add a task/i))
    // No title typed — the chip should be inert.
    await user.click(screen.getByRole('button', { name: '30m' }))

    // Still just the initial GET; no POST fired, and the sheet is still open.
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(screen.getByPlaceholderText(/what is it/i)).toBeInTheDocument()
  })

  describe('status filters', () => {
    function stubTasks() {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([
            rawTask({ id: '1', title: 'Active one', status: 'active' }),
            rawTask({ id: '2', title: 'Active two', status: 'active' }),
            rawTask({ id: '3', title: 'Unprocessed one', status: 'unprocessed' }),
            rawTask({ id: '4', title: 'Stale one', status: 'stale' }),
            rawTask({ id: '5', title: 'Done one', status: 'done' }),
          ]),
        ),
      )
    }

    it('the four status filters render with their counts, and the selected one is aria-pressed', async () => {
      stubTasks()
      render(<TasksScreen />)

      const active = await screen.findByRole('button', { name: 'Active 2' })
      expect(active).toHaveAttribute('aria-pressed', 'true')
      expect(screen.getByRole('button', { name: 'Unprocessed 1' })).toHaveAttribute('aria-pressed', 'false')
      expect(screen.getByRole('button', { name: 'Stale 1' })).toHaveAttribute('aria-pressed', 'false')
      expect(screen.getByRole('button', { name: 'Done 1' })).toHaveAttribute('aria-pressed', 'false')
    })

    it('selecting a filter shows that pile and marks it pressed', async () => {
      const user = userEvent.setup()
      stubTasks()
      render(<TasksScreen />)
      await screen.findByText('Active one')

      await user.click(screen.getByRole('button', { name: 'Stale 1' }))

      expect(await screen.findByText('Stale one')).toBeInTheDocument()
      expect(screen.queryByText('Active one')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Stale 1' })).toHaveAttribute('aria-pressed', 'true')
      expect(screen.getByRole('button', { name: 'Active 2' })).toHaveAttribute('aria-pressed', 'false')
    })

    it('status is read off the wire — a Task the age rule would call Active still lands in the pile its wire status names', async () => {
      // Freshly created "now", eligible, no completions — every naive client-side heuristic
      // would call this Active. The wire says Stale, and that's what must win.
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Looks fresh', status: 'stale', createdAt: new Date().toISOString() })]),
        ),
      )
      const user = userEvent.setup()
      render(<TasksScreen />)
      await screen.findByRole('button', { name: 'Stale 1' })

      await user.click(screen.getByRole('button', { name: 'Stale 1' }))

      expect(await screen.findByText('Looks fresh')).toBeInTheDocument()
    })
  })

  describe('row markers', () => {
    it('a Task with no Duration renders a "no duration" marker and its mark-off control is disabled', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'No dur task', duration: null, status: 'unprocessed' })]),
        ),
      )
      const user = userEvent.setup()
      render(<TasksScreen />)
      await user.click(await screen.findByRole('button', { name: /^Unprocessed /i }))

      expect(await screen.findByText('no duration')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /mark no dur task done/i })).toBeDisabled()
    })

    it('an Orphan badge renders only on an Active Task, so it never co-occurs with the unprocessed or stale piles', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([
            rawTask({ id: '1', title: 'Orphan active', status: 'active', zeroKind: 'orphan' }),
            rawTask({ id: '2', title: 'Orphan-ish unprocessed', status: 'unprocessed', zeroKind: 'orphan' }),
          ]),
        ),
      )
      const user = userEvent.setup()
      render(<TasksScreen />)

      expect(await screen.findByText('Orphan active')).toBeInTheDocument()
      expect(screen.getByText('orphan')).toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: /^Unprocessed /i }))
      expect(await screen.findByText('Orphan-ish unprocessed')).toBeInTheDocument()
      expect(screen.queryByText('orphan')).not.toBeInTheDocument()
    })

    it('the Orphan badge carries no link — the window-editor deep-link payload is not on the wire (#174)', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Orphan active', status: 'active', zeroKind: 'orphan' })]),
        ),
      )
      render(<TasksScreen />)

      await screen.findByText('Orphan active')
      expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })

    it('a Done Task strikes its title and disables its tick — re-tapping cannot double-complete it', async () => {
      const user = userEvent.setup()
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Done task', status: 'done', duration: '10' })]),
        ),
      )
      render(<TasksScreen />)
      await user.click(await screen.findByRole('button', { name: /^Done /i }))

      const tick = await screen.findByRole('button', { name: /mark done task done/i })
      expect(tick).toBeDisabled()
      expect(tick).toHaveAttribute('data-done', '1')
      expect(screen.getByText('Done task')).toHaveStyle({ textDecoration: 'line-through' })
    })

    it('a deferred Task is present in this list, marked with its surface date, so it stays findable', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([
            rawTask({ id: '1', title: 'Deferred task', status: 'active', eligible: false, defer: '2026-10-01' }),
          ]),
        ),
      )
      render(<TasksScreen />)

      expect(await screen.findByText('Deferred task')).toBeInTheDocument()
      expect(screen.getByText(/2026-10-01/)).toBeInTheDocument()
    })
  })

  describe('"Not now"', () => {
    it('renders for an eligible, non-recurring, non-derived Active Task', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Postponable', status: 'active', eligible: true })]),
        ),
      )
      render(<TasksScreen />)

      expect(await screen.findByRole('button', { name: /not now/i })).toBeInTheDocument()
    })

    it('is gated on eligibility, not the Active label alone — a deferred (ineligible) Active Task gets no gesture', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([
            rawTask({ id: '1', title: 'Deferred task', status: 'active', eligible: false, defer: '2026-10-01' }),
          ]),
        ),
      )
      render(<TasksScreen />)

      await screen.findByText('Deferred task')
      expect(screen.queryByRole('button', { name: /not now/i })).not.toBeInTheDocument()
    })

    it('never appears on a recurring Task', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Recurring task', status: 'active', eligible: true, recurring: true })]),
        ),
      )
      render(<TasksScreen />)

      await screen.findByText('Recurring task')
      expect(screen.queryByRole('button', { name: /not now/i })).not.toBeInTheDocument()
    })

    it('never appears on a derived Task', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([rawTask({ id: '1', title: 'Derived task', status: 'active', eligible: true, derived: true })]),
        ),
      )
      render(<TasksScreen />)

      await screen.findByText('Derived task')
      expect(screen.queryByRole('button', { name: /not now/i })).not.toBeInTheDocument()
    })

    it("carries the Task's title in its accessible name, distinguishing two eligible rows' identical-looking gestures", async () => {
      const user = userEvent.setup()
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          jsonResponse([
            rawTask({ id: '1', title: 'Water the plants', status: 'active', eligible: true }),
            rawTask({ id: '2', title: 'File the receipt', status: 'active', eligible: true }),
          ]),
        ),
      )
      render(<TasksScreen />)
      await screen.findByText('Water the plants')

      expect(screen.getByRole('button', { name: /not now.*water the plants/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /not now.*file the receipt/i })).toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: /not now.*water the plants/i }))
      expect(screen.getByRole('button', { name: /tomorrow.*water the plants/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /a week.*water the plants/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /a month.*water the plants/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /postpone.*water the plants/i })).toBeInTheDocument()
    })
  })

  describe('postpone', () => {
    const now = new Date('2026-09-20T12:00:00Z')

    function stubPostponable(overrides: Record<string, unknown> = {}) {
      return rawTask({ id: '1', title: 'Postponable', status: 'active', eligible: true, ...overrides })
    }

    it('offers three fixed intervals plus a "Pick a date…" escape', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([stubPostponable()])))
      const user = userEvent.setup()
      render(<TasksScreen now={now} />)
      await user.click(await screen.findByRole('button', { name: /not now/i }))

      expect(screen.getByRole('button', { name: /tomorrow/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /^a week/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /^a month/i })).toBeInTheDocument()
      expect(screen.getByLabelText('Pick a date…')).toBeInTheDocument()
    })

    it('a postponed row stays in place, greyed, showing its postpone date', async () => {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(jsonResponse([stubPostponable()]))
        .mockResolvedValueOnce(new Response(null, { status: 204 }))
        .mockResolvedValueOnce(jsonResponse([stubPostponable({ postpone: '2026-09-21', eligible: false })]))
      vi.stubGlobal('fetch', fetchMock)
      const user = userEvent.setup()
      render(<TasksScreen now={now} />)
      await user.click(await screen.findByRole('button', { name: /not now/i }))
      await user.click(screen.getByRole('button', { name: /tomorrow/i }))

      const row = (await screen.findByText('Postponable')).closest('.row') as HTMLElement
      expect(row).toBeInTheDocument()
      expect(row).toHaveStyle({ opacity: '.45' })
      expect(screen.getByText(/2026-09-21/)).toBeInTheDocument()

      const [, postponeCall] = fetchMock.mock.calls
      expect(postponeCall[0]).toBe('/api/tasks/1/postpone')
      expect(postponeCall[1]?.method).toBe('PUT')
      expect(JSON.parse(postponeCall[1]?.body as string)).toEqual({ date: '2026-09-21' })
    })

    it('a Postpone interval landing past the Deadline is labelled at the point of the tap, and the row says so too', async () => {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(jsonResponse([stubPostponable({ deadline: '2026-09-25' })]))
        .mockResolvedValueOnce(new Response(null, { status: 204 }))
        .mockResolvedValueOnce(
          jsonResponse([stubPostponable({ deadline: '2026-09-25', postpone: '2026-10-20', eligible: false })]),
        )
      vi.stubGlobal('fetch', fetchMock)
      const user = userEvent.setup()
      render(<TasksScreen now={now} />)
      await user.click(await screen.findByRole('button', { name: /not now/i }))

      // "a month" from 2026-09-20 is 2026-10-20, past the 2026-09-25 deadline.
      expect(screen.getByRole('button', { name: /a month.*past its deadline/i })).toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: /a month/i }))
      await screen.findByText(/2026-10-20/)
      expect(screen.getByText(/past its deadline/i)).toBeInTheDocument()
    })

    it("the pick-a-date escape survives its own input event — same DOM node before and after", async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([stubPostponable()])))
      const user = userEvent.setup()
      render(<TasksScreen now={now} />)
      await user.click(await screen.findByRole('button', { name: /not now/i }))

      const input = screen.getByLabelText('Pick a date…')
      fireEvent.change(input, { target: { value: '2026-09-30' } })
      expect(screen.getByLabelText('Pick a date…')).toBe(input)
      expect(input).toHaveValue('2026-09-30')
    })

    // There is no client-side clock (tests/TEST-INVENTORY.md § Web-Now): whether a stored
    // Postpone or Defer has elapsed is a predicate the server already answers via `eligible`
    // (StatusRules.IsEligible's `now >= Postpone` / `now >= Defer` conjunction), so the row reads
    // that field rather than comparing `t.postpone`/`t.defer` against a locally-resolved `today`.
    it('an elapsed Postpone (now eligible again) renders ungreyed, with no postponed pill, and offers "Not now"', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(jsonResponse([stubPostponable({ postpone: '2026-09-01', eligible: true })])),
      )
      render(<TasksScreen now={now} />)

      const row = (await screen.findByText('Postponable')).closest('.row') as HTMLElement
      expect(row).not.toHaveStyle({ opacity: '.45' })
      expect(screen.queryByText(/postponed to/i)).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: /not now/i })).toBeInTheDocument()
    })

    it('"A month" clamps to the target month\'s last day at a month end, rather than overflowing', async () => {
      // Chicago is CST (UTC-6) in January — no DST — so 18:00 UTC on the 31st is still
      // 2026-01-31 locally.
      const monthEndNow = new Date('2026-01-31T18:00:00Z')
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(jsonResponse([stubPostponable()]))
        .mockResolvedValueOnce(new Response(null, { status: 204 }))
        .mockResolvedValueOnce(jsonResponse([stubPostponable({ postpone: '2026-02-28', eligible: false })]))
      vi.stubGlobal('fetch', fetchMock)
      const user = userEvent.setup()
      render(<TasksScreen now={monthEndNow} />)
      await user.click(await screen.findByRole('button', { name: /not now/i }))
      await user.click(screen.getByRole('button', { name: /^a month/i }))

      const [, postponeCall] = fetchMock.mock.calls
      // 2026-01-31 + "a month" must land on 2026-02-28 (Feb's last day), never 2026-03-03.
      expect(JSON.parse(postponeCall[1]?.body as string)).toEqual({ date: '2026-02-28' })
    })

    it('an elapsed Defer (now eligible again) renders with no "surfaces" pill', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(jsonResponse([stubPostponable({ defer: '2026-09-01', eligible: true })])),
      )
      render(<TasksScreen now={now} />)

      await screen.findByText('Postponable')
      expect(screen.queryByText(/surfaces/i)).not.toBeInTheDocument()
    })
  })

  describe('failed writes', () => {
    it('a failed mark-off renders a note naming the Task, and the note survives a reload that also fails', async () => {
      const fetchMock = vi
        .fn()
        // initial GET
        .mockResolvedValueOnce(jsonResponse([rawTask({ id: '1', title: 'Water the plants', duration: '10' })]))
        // POST completions rejects
        .mockRejectedValueOnce(new Error('offline'))
        // the reload it triggers fails too
        .mockRejectedValueOnce(new Error('offline'))
      vi.stubGlobal('fetch', fetchMock)
      const user = userEvent.setup()
      render(<TasksScreen />)

      await user.click(await screen.findByRole('button', { name: /mark water the plants done/i }))

      const alert = await screen.findByRole('alert')
      expect(alert).toHaveTextContent(/Water the plants/)
      // The failed reload replaces the ready-state body with the error state — the note must
      // still be showing, not have vanished along with the rows it used to render inside.
      expect(await screen.findByText(/couldn.t load tasks/i)).toBeInTheDocument()
      expect(screen.getByRole('alert')).toHaveTextContent(/Water the plants/)
    })

    it("a refused postpone renders the server's reason appended to the sentence", async () => {
      const fetchMock = vi
        .fn()
        // initial GET
        .mockResolvedValueOnce(
          jsonResponse([rawTask({ id: '1', title: 'Postponable', status: 'active', eligible: true })]),
        )
        // PUT postpone refused — #138's { error } body
        .mockResolvedValueOnce(new Response(JSON.stringify({ error: 'already done' }), { status: 409 }))
        // reload after the failure
        .mockResolvedValueOnce(
          jsonResponse([rawTask({ id: '1', title: 'Postponable', status: 'active', eligible: true })]),
        )
      vi.stubGlobal('fetch', fetchMock)
      const user = userEvent.setup()
      render(<TasksScreen />)

      await user.click(await screen.findByRole('button', { name: /not now/i }))
      await user.click(screen.getByRole('button', { name: /tomorrow/i }))

      const alert = await screen.findByRole('alert')
      expect(alert).toHaveTextContent(/Postponable/)
      expect(alert).toHaveTextContent(/already done/)
    })
  })

  describe('opening task detail', () => {
    it('a task row opens that task\'s detail as a pushed screen, with a back control to the list', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(jsonResponse([rawTask({ id: '1', title: 'Water the plants' })])),
      )
      const push = vi.fn<(screen: PushedScreen) => void>()
      render(
        <PushContext.Provider value={push}>
          <TasksScreen />
        </PushContext.Provider>,
      )

      const title = await screen.findByRole('button', { name: 'Open Water the plants' })
      fireEvent.click(title)

      expect(push).toHaveBeenCalledTimes(1)
      const pushed = push.mock.calls[0][0]
      expect(pushed.backLabel).toBe('Tasks')
      expect(pushed.node).toMatchObject({ type: TaskDetail, props: { taskId: '1' } })
    })
  })

  it('renders "Nothing here." for an empty selected pile', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse([rawTask({ id: '1', title: 'Only active', status: 'active' })])),
    )
    render(<TasksScreen />)
    await screen.findByText('Only active')

    await user.click(screen.getByRole('button', { name: /^Done /i }))

    expect(await screen.findByText('Nothing here.')).toBeInTheDocument()
  })
})
