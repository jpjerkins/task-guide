import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskDetail } from './TaskDetail'

// Same fetch-stub-at-the-network-boundary approach as TasksScreen.test.tsx: two endpoints
// (GET /api/tasks/{id}, GET /api/dimensions) is still small enough that per-test stubbing beats
// standing up MSW.
function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
}

// A full raw wire TaskResponse, with every field defaulted so a fixture need only name the
// fields a given test cares about. Copied from TasksScreen.test.tsx's rawTask with the #174
// detail fields (notes/dimensions/looseTags/opportunities/patternWeekCount/orphanBlameDimensions)
// added — the two Web lanes deliberately don't share test fixtures either.
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
    orphanBlameDimensions: [],
    ...overrides,
  }
}

const DIMENSIONS = [
  { id: 'location', label: 'Location', algebra: 'categorical', values: ['home', 'garage'], taskDefault: null, windowDefault: null, source: 'authored' },
  { id: 'withWhom', label: 'With whom', algebra: 'categorical', values: ['alone', 'family'], taskDefault: null, windowDefault: null, source: 'authored' },
  { id: 'weather', label: 'Weather', algebra: 'categorical', values: ['any', 'dry'], taskDefault: null, windowDefault: null, source: 'authored' },
  { id: 'energy', label: 'Energy', algebra: 'ordinal', values: ['low', 'high'], taskDefault: 'low', windowDefault: 'high', source: 'authored' },
  { id: 'duration', label: 'Duration', algebra: 'ordinal', values: ['2', '10', '30', '60', 'longer'], taskDefault: null, windowDefault: null, source: 'authored' },
]

// Stubs both GET /api/tasks/{id} and GET /api/dimensions off one fetch mock, routed by URL.
function stub(task: Record<string, unknown>, dimensions: unknown[] = DIMENSIONS) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url === '/api/dimensions') return Promise.resolve(jsonResponse(dimensions))
      return Promise.resolve(jsonResponse(task))
    }),
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('TaskDetail — fit bar', () => {
  it('renders a count plus its horizon in words: "in the next 7 days" with no Deadline', async () => {
    stub(rawTask({ opportunities: 3, patternWeekCount: 3, deadline: null }))
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByText('3', { selector: '.n' })).toBeInTheDocument()
    expect(screen.getByText(/in the next 7 days/i)).toBeInTheDocument()
  })

  it('renders "before it is due" when the Task carries a Deadline still ahead', async () => {
    const now = new Date('2026-09-21T12:00:00Z')
    stub(rawTask({ opportunities: 2, patternWeekCount: 2, deadline: '2026-09-25' }))
    render(<TaskDetail taskId="1" now={now} />)

    expect(await screen.findByText('2', { selector: '.n' })).toBeInTheDocument()
    expect(screen.getByText(/before it is due/i)).toBeInTheDocument()
    expect(screen.queryByText(/in the next 7 days/i)).not.toBeInTheDocument()
  })

  it('distinguishes the orphan zero from the "nothing wrong" zero', async () => {
    stub(rawTask({ opportunities: 0, patternWeekCount: 0, zeroKind: 'orphan', orphanBlameDimensions: [] }))
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByText(/no single property is to blame/i)).toBeInTheDocument()
  })

  it('the "nothing wrong" zero names the override/event reading, not orphan', async () => {
    stub(rawTask({ opportunities: 0, patternWeekCount: 4, zeroKind: 'noneInThisStretch' }))
    render(<TaskDetail taskId="1" />)

    expect(
      await screen.findByText(/an override or event has taken them all out of this stretch/i),
    ).toBeInTheDocument()
    expect(screen.queryByText(/no single property is to blame/i)).not.toBeInTheDocument()
  })

  it('the orphan reading names the blamed Dimensions by label', async () => {
    stub(
      rawTask({
        opportunities: 0,
        patternWeekCount: 0,
        zeroKind: 'orphan',
        orphanBlameDimensions: ['location', 'withWhom'],
      }),
    )
    render(<TaskDetail taskId="1" />)

    const which = await screen.findByText(/no window declares/i)
    expect(which).toHaveTextContent(/Location or With whom/)
  })

  it('an empty blame list reads as no single property being to blame, not an empty list', async () => {
    stub(rawTask({ opportunities: 0, patternWeekCount: 0, zeroKind: 'orphan', orphanBlameDimensions: [] }))
    render(<TaskDetail taskId="1" />)

    expect(
      await screen.findByText(/no single property is to blame; the combination has no home in this pattern/i),
    ).toBeInTheDocument()
  })

  it('an overdue Task reverts to the plain rolling 7 days, not "before it is due"', async () => {
    const now = new Date('2026-09-21T12:00:00Z')
    stub(rawTask({ opportunities: 2, patternWeekCount: 2, deadline: '2026-09-10' }))
    render(<TaskDetail taskId="1" now={now} />)

    expect(await screen.findByText(/in the next 7 days/i)).toBeInTheDocument()
    expect(screen.queryByText(/before it is due/i)).not.toBeInTheDocument()
  })

  it('an unknown zeroKind renders as unknown, never as zero', async () => {
    // OrphanDetection.KindOfZero returns Unknown exactly when opportunities is null on an Active
    // Task — the server never emits `{ opportunities: 0, zeroKind: 'unknown' }`. This is the same
    // "opportunities: null" shape a deferred/postponed Task carries, and the two must render
    // distinguishably (see the deferred/postponed test just below).
    stub(rawTask({ opportunities: null, patternWeekCount: 3, zeroKind: 'unknown' }))
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByText(/unknown/i)).toBeInTheDocument()
    expect(screen.queryByText(/^0$/)).not.toBeInTheDocument()
  })

  it('a deferred/postponed Task (opportunities null) renders no count and neither zero reading', async () => {
    stub(rawTask({ eligible: false, defer: '2026-10-01', opportunities: null, patternWeekCount: 4, zeroKind: null }))
    render(<TaskDetail taskId="1" />)

    await waitFor(() => expect(screen.queryByText(/loading/i)).not.toBeInTheDocument())
    expect(screen.queryByText(/in the next 7 days/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/before it is due/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/nothing is wrong with the task/i)).not.toBeInTheDocument()
  })

  it('a deferred Task with an orphan patternWeekCount still renders the orphan reading', async () => {
    stub(
      rawTask({
        eligible: false,
        defer: '2026-10-01',
        opportunities: null,
        patternWeekCount: 0,
        zeroKind: 'orphan',
        orphanBlameDimensions: [],
      }),
    )
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByText(/no single property is to blame/i)).toBeInTheDocument()
  })

  it('an Unprocessed Task renders orphan-ness as undefined, not as a count', async () => {
    stub(rawTask({ status: 'unprocessed', duration: null, opportunities: null, patternWeekCount: null, zeroKind: null }))
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByText(/undefined/i)).toBeInTheDocument()
    expect(screen.queryByText(/no window declares|no single property is to blame/i)).not.toBeInTheDocument()
  })

  it('a Stale Task renders no orphan badge', async () => {
    stub(rawTask({ status: 'stale', opportunities: null, patternWeekCount: null, zeroKind: null }))
    render(<TaskDetail taskId="1" />)

    await waitFor(() => expect(screen.queryByText(/loading/i)).not.toBeInTheDocument())
    expect(screen.queryByText(/orphan/i)).not.toBeInTheDocument()
  })
})

// GET /api/tasks/{id} calls carry no `init` (getJson calls `fetch(path)` bare); writes always
// carry a `method`. Routes by URL and consumes each endpoint's response queue in order, so a test
// can name the initial read and the post-write re-read separately without depending on Promise.all
// call ordering beyond "GET task, then GET dimensions" (the order the component's own code issues
// them in).
function makeFetchMock(options: {
  taskResponses: unknown[]
  dimensionsResponses?: unknown[]
  writes?: { match: (url: string, init: RequestInit | undefined) => boolean; response: Response }[]
}) {
  const { taskResponses, dimensionsResponses = [DIMENSIONS], writes = [] } = options
  let taskIdx = 0
  let dimIdx = 0
  return vi.fn((url: string, init?: RequestInit) => {
    if (url === '/api/dimensions' && !init) {
      const body = dimensionsResponses[Math.min(dimIdx, dimensionsResponses.length - 1)]
      dimIdx++
      return Promise.resolve(jsonResponse(body))
    }
    if (url === '/api/tasks/1' && !init) {
      const body = taskResponses[Math.min(taskIdx, taskResponses.length - 1)]
      taskIdx++
      return Promise.resolve(jsonResponse(body))
    }
    const write = writes.find((w) => w.match(url, init))
    if (write) return Promise.resolve(write.response)
    throw new Error(`Unhandled fetch: ${init?.method ?? 'GET'} ${url}`)
  })
}

describe('TaskDetail — form', () => {
  it('renders title, notes, the Duration chipset, Deadline, and one chipset per Dimension', async () => {
    vi.stubGlobal(
      'fetch',
      makeFetchMock({
        taskResponses: [
          rawTask({ title: 'Water the plants', notes: 'A note', duration: '30', deadline: '2026-10-01', dimensions: { location: ['home'] } }),
        ],
      }),
    )
    render(<TaskDetail taskId="1" />)

    expect(await screen.findByDisplayValue('Water the plants')).toBeInTheDocument()
    expect(screen.getByDisplayValue('A note')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '30m' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByLabelText('Deadline')).toHaveValue('2026-10-01')
    // "Duration" appears once — the chipset control, not the same-named Dimension in the registry.
    expect(screen.getAllByText('Duration')).toHaveLength(1)
    expect(screen.getByText('Location')).toBeInTheDocument()
    expect(screen.getByText('Energy')).toBeInTheDocument()
  })

  it("the Deadline date entry survives its own input event — same DOM node", async () => {
    vi.stubGlobal('fetch', makeFetchMock({ taskResponses: [rawTask({ deadline: '2026-09-01' })] }))
    render(<TaskDetail taskId="1" />)

    const input = await screen.findByLabelText('Deadline')
    fireEvent.change(input, { target: { value: '2026-09-30' } })
    expect(screen.getByLabelText('Deadline')).toBe(input)
    expect(input).toHaveValue('2026-09-30')
  })

  it('the Dimensions section offers no add/remove/rename control', async () => {
    vi.stubGlobal('fetch', makeFetchMock({ taskResponses: [rawTask()] }))
    render(<TaskDetail taskId="1" />)

    await screen.findByText('Location')
    expect(screen.queryByRole('button', { name: /add|new dimension|remove|rename/i })).not.toBeInTheDocument()
  })

  it('loose Tags render as kept-but-inert pills under "Unresolved", distinct from matched values', async () => {
    vi.stubGlobal('fetch', makeFetchMock({ taskResponses: [rawTask({ looseTags: ['errand'] })] }))
    render(<TaskDetail taskId="1" />)

    const pill = await screen.findByText(/errand.*kept but inert/i)
    expect(pill.tagName).not.toBe('BUTTON')
    expect(screen.getByText('Unresolved')).toBeInTheDocument()
  })

  it('clearing Postpone DELETEs and re-reads, and the control then disappears', async () => {
    const fetchMock = makeFetchMock({
      taskResponses: [rawTask({ postpone: '2026-09-25', eligible: false }), rawTask({ postpone: null, eligible: true })],
      writes: [
        {
          match: (url, init) => url === '/api/tasks/1/postpone' && init?.method === 'DELETE',
          response: new Response(null, { status: 204 }),
        },
      ],
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<TaskDetail taskId="1" />)

    const clearBtn = await screen.findByRole('button', { name: /clear postpone/i })
    await user.click(clearBtn)

    await waitFor(() => expect(screen.queryByRole('button', { name: /clear postpone/i })).not.toBeInTheDocument())
    expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/tasks/1/postpone' && init?.method === 'DELETE')).toBe(true)
  })

  it('a recurring Task offers only Defer\'s offset form — no date control for Defer', async () => {
    vi.stubGlobal('fetch', makeFetchMock({ taskResponses: [rawTask({ recurring: true })] }))
    render(<TaskDetail taskId="1" />)

    await screen.findByRole('button', { name: /defer/i })
    // The only date input on the whole screen is Deadline's — Defer's offset form has none.
    expect(screen.getAllByDisplayValue(/^\d{4}-\d{2}-\d{2}$|^$/).filter((el) => el.getAttribute('type') === 'date')).toHaveLength(1)
    expect(screen.getByLabelText(/offset/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/unit/i)).toBeInTheDocument()
  })

  it('Defer PATCHes {date: null, offset, unit} and re-reads', async () => {
    const fetchMock = makeFetchMock({
      taskResponses: [rawTask({ recurring: true, defer: null }), rawTask({ recurring: true, defer: '2026-09-28' })],
      writes: [
        {
          match: (url, init) => url === '/api/tasks/1' && init?.method === 'PATCH',
          response: new Response(null, { status: 204 }),
        },
      ],
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<TaskDetail taskId="1" />)

    await user.type(await screen.findByLabelText(/offset/i), '2')
    await user.selectOptions(screen.getByLabelText(/unit/i), 'Weeks')
    await user.click(screen.getByRole('button', { name: /defer/i }))

    await waitFor(() => {
      const patchCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/tasks/1' && init?.method === 'PATCH')
      expect(patchCall).toBeDefined()
      expect(JSON.parse((patchCall as [string, RequestInit])[1].body as string)).toEqual({ date: null, offset: 2, unit: 1 })
    })
  })

  it('Save PUTs the whole form in one request and re-reads', async () => {
    const fetchMock = makeFetchMock({
      taskResponses: [
        rawTask({ title: 'Old title', notes: null, duration: '10', deadline: null, dimensions: {} }),
        rawTask({ title: 'New title', notes: null, duration: '10', deadline: null, dimensions: {} }),
      ],
      writes: [
        {
          match: (url, init) => url === '/api/tasks/1' && init?.method === 'PUT',
          response: new Response(null, { status: 204 }),
        },
      ],
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<TaskDetail taskId="1" />)

    const titleInput = await screen.findByDisplayValue('Old title')
    await user.clear(titleInput)
    await user.type(titleInput, 'New title')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    await screen.findByDisplayValue('New title')
    const putCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/tasks/1' && init?.method === 'PUT')
    expect(putCall).toBeDefined()
    expect(JSON.parse((putCall as [string, RequestInit])[1].body as string)).toEqual({
      title: 'New title',
      notes: null,
      duration: '10',
      deadline: null,
      dimensions: {},
    })
  })

  it('Save excludes `duration` from the dimensions payload — the server rejects it there', async () => {
    // TaskEndpoints.ToResponse builds `dimensions` from Task.Tags.Dimensions, and Duration IS a
    // declared Dimension (KnownDimensions.Default), so any Task with a Duration arrives with
    // `dimensions: { duration: [...], ... }` on the wire. UpdateTaskDetails.Invalid refuses a
    // write whose `dimensions` still carries that key ("duration belongs in the duration field") —
    // so every Task with a Duration (every Active Task) would fail Save if it were sent verbatim.
    const fetchMock = makeFetchMock({
      taskResponses: [
        rawTask({ title: 'A task', duration: '30', dimensions: { duration: ['30'], location: ['home'] } }),
        rawTask({ title: 'A task', duration: '30', dimensions: { duration: ['30'], location: ['home'] } }),
      ],
      writes: [
        {
          match: (url, init) => url === '/api/tasks/1' && init?.method === 'PUT',
          response: new Response(null, { status: 204 }),
        },
      ],
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<TaskDetail taskId="1" />)

    await screen.findByDisplayValue('A task')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    await waitFor(() => {
      const putCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/tasks/1' && init?.method === 'PUT')
      expect(putCall).toBeDefined()
      const body = JSON.parse((putCall as [string, RequestInit])[1].body as string)
      expect(body.dimensions).toEqual({ location: ['home'] })
    })
  })

  it("a refused Save renders the server's reason in the alert note", async () => {
    const fetchMock = makeFetchMock({
      taskResponses: [rawTask({ title: 'A task' }), rawTask({ title: 'A task' })],
      writes: [
        {
          match: (url, init) => url === '/api/tasks/1' && init?.method === 'PUT',
          response: new Response(JSON.stringify({ error: 'duration belongs in the duration field' }), { status: 400 }),
        },
      ],
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<TaskDetail taskId="1" />)

    await screen.findByDisplayValue('A task')
    await user.click(screen.getByRole('button', { name: /^save$/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/duration belongs in the duration field/i)
  })
})
