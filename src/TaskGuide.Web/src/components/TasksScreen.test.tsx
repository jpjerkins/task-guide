import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('TasksScreen', () => {
  it("renders a task's title and duration from a stubbed GET /api/tasks", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([{ id: '1', title: 'Water the plants', duration: 10 }]),
      ),
    )

    render(<TasksScreen />)

    expect(await screen.findByText('Water the plants')).toBeInTheDocument()
    expect(screen.getByText('10m')).toBeInTheDocument()
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
        jsonResponse([{ id: '2', title: 'Call the vet', duration: 30 }]),
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
})
