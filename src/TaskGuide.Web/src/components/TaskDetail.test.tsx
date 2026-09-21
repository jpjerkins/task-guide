import { render, screen, waitFor } from '@testing-library/react'
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

    expect(await screen.findByText(/3/)).toBeInTheDocument()
    expect(screen.getByText(/in the next 7 days/i)).toBeInTheDocument()
  })

  it('renders "before it is due" when the Task carries a Deadline still ahead', async () => {
    const now = new Date('2026-09-21T12:00:00Z')
    stub(rawTask({ opportunities: 2, patternWeekCount: 2, deadline: '2026-09-25' }))
    render(<TaskDetail taskId="1" now={now} />)

    expect(await screen.findByText(/2/)).toBeInTheDocument()
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

    expect(await screen.findByText(/no window declares/i)).toBeInTheDocument()
    expect(screen.getByText(/Location/)).toBeInTheDocument()
    expect(screen.getByText(/With whom/)).toBeInTheDocument()
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
    stub(rawTask({ opportunities: 0, patternWeekCount: 0, zeroKind: 'unknown' }))
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
