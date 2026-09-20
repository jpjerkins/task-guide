import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TriageScreen } from './TriageScreen'

function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
}

let unprocessed: unknown[]
let stale: unknown[]

function routedFetch(url: string): Promise<Response> {
  if (url === '/api/tasks?status=unprocessed') return Promise.resolve(jsonResponse(unprocessed))
  if (url === '/api/tasks?status=stale') return Promise.resolve(jsonResponse(stale))
  return Promise.resolve(new Response(null, { status: 404 }))
}

beforeEach(() => {
  vi.restoreAllMocks()
  unprocessed = []
  stale = []
})

describe('TriageScreen', () => {
  it('renders the two piles under their own headings, with both counts in the subtitle', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    stale = [
      { id: 's1', title: 'Reorganize garage', duration: '30', createdAt: '2026-08-01T00:00:00Z' },
      { id: 's2', title: 'Learn Welsh', duration: null, createdAt: '2026-08-01T00:00:00Z' },
    ]
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)

    expect(await screen.findByText('1 unprocessed · 2 stale')).toBeInTheDocument()
    expect(screen.getByText('Missing a duration')).toBeInTheDocument()
    expect(screen.getByText('Stale — reword, slice smaller, or delete')).toBeInTheDocument()
    expect(screen.getByText('File the receipt')).toBeInTheDocument()
    expect(screen.getByText('Reorganize garage')).toBeInTheDocument()
    expect(screen.getByText('Learn Welsh')).toBeInTheDocument()
  })

  it('an unprocessed row renders the five Duration buttons inline', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    for (const label of ['2m', '10m', '30m', '60m', 'Longer']) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
    }
  })

  it('pressing one moves the Task out of the unprocessed pile without the screen being re-entered', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    const durationWrites: RequestInit[] = []
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        durationWrites.push(init)
        unprocessed = []
        return new Response(null, { status: 204 })
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')
    // Captured before the click: if the screen were torn down and re-entered rather than just
    // re-reading its two lists, React would rebuild this node and the reference would change.
    const navBeforeClick = document.querySelector('.nav')

    await user.click(screen.getByRole('button', { name: '2m' }))

    await waitFor(() => expect(screen.queryByText('File the receipt')).not.toBeInTheDocument())
    expect(durationWrites).toHaveLength(1)
    expect(JSON.parse(String(durationWrites[0].body))).toEqual({ duration: '2' })
    expect(document.querySelector('.nav')).toBe(navBeforeClick)
  })

  it('the stale pile renders ordinary Task rows and offers no "un-stale" control', async () => {
    stale = [{ id: 's1', title: 'Reorganize garage', duration: '30', createdAt: '2026-08-01T00:00:00Z' }]
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)
    await screen.findByText('Reorganize garage')

    expect(screen.getByText('30m')).toBeInTheDocument()
    // No unprocessed Tasks in this test, so any button on the page would have to belong to the
    // stale row — asserting there are none proves the stale row itself offers no control at all.
    expect(screen.queryAllByRole('button')).toHaveLength(0)
  })

  it('an empty pile renders "Nothing to process." rather than being hidden', async () => {
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)
    await screen.findByText('Missing a duration')

    // The inventory names exactly one empty-pile string and is silent on whether an empty stale
    // pile gets a different one — this reads it as the same literal for both piles.
    expect(await screen.findAllByText('Nothing to process.')).toHaveLength(2)
    expect(screen.getByText('Stale — reword, slice smaller, or delete')).toBeInTheDocument()
  })

  it('nothing on this screen subscribes to a notification; these two piles nudge only through the reminder footer', async () => {
    const fetchMock = vi.fn(routedFetch)
    vi.stubGlobal('fetch', fetchMock)
    const notificationCtor = vi.fn()
    vi.stubGlobal('Notification', notificationCtor)
    const swRegister = vi.fn()
    Object.defineProperty(navigator, 'serviceWorker', {
      value: { register: swRegister },
      configurable: true,
    })

    render(<TriageScreen />)
    await screen.findByText('Missing a duration')

    expect(notificationCtor).not.toHaveBeenCalled()
    expect(swRegister).not.toHaveBeenCalled()
    const urls = fetchMock.mock.calls.map(([url]) => url)
    expect(new Set(urls)).toEqual(new Set(['/api/tasks?status=unprocessed', '/api/tasks?status=stale']))
  })

  it('a failed read lands on the connection-error state rather than presenting empty piles', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))

    render(<TriageScreen />)

    expect(await screen.findByText(/couldn.t load tasks/i)).toBeInTheDocument()
    expect(screen.queryByText('Nothing to process.')).not.toBeInTheDocument()
  })

  it('a refused duration write states that it failed rather than silently re-reading', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        return new Response(null, { status: 500 })
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getByRole('button', { name: '2m' }))

    expect(await screen.findByText("Couldn't set this task's duration.")).toBeInTheDocument()
    // Untouched by the refused write — still in the unprocessed pile, buttons re-enabled.
    expect(screen.getByText('File the receipt')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '2m' })).not.toBeDisabled()
  })

  it('renders `.nav` and `.scroll` as siblings with no wrapping element, so `.scroll`\'s flex sizing applies', async () => {
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    const { container } = render(<TriageScreen />)
    await screen.findByText('Missing a duration')

    // `.device`/`.scroll` (index.css) only make a scroll area when `.scroll` is a direct flex
    // child of `.device` — an intervening unstyled wrapper breaks that chain silently.
    expect(container.querySelector(':scope > .nav')).not.toBeNull()
    expect(container.querySelector(':scope > .scroll')).not.toBeNull()
  })

  it('renders a loading state before the first read resolves, rather than presenting empty piles', async () => {
    let resolveUnprocessed!: (r: Response) => void
    let resolveStale!: (r: Response) => void
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        if (url === '/api/tasks?status=unprocessed') {
          return new Promise<Response>((res) => {
            resolveUnprocessed = res
          })
        }
        if (url === '/api/tasks?status=stale') {
          return new Promise<Response>((res) => {
            resolveStale = res
          })
        }
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    render(<TriageScreen />)

    expect(await screen.findByText(/loading/i)).toBeInTheDocument()
    expect(screen.queryByText('Nothing to process.')).not.toBeInTheDocument()

    resolveUnprocessed(jsonResponse([]))
    resolveStale(jsonResponse([]))
    await waitFor(() => expect(screen.getAllByText('Nothing to process.')).toHaveLength(2))
  })

  it('does not show pile counts in the subtitle when the read failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))

    render(<TriageScreen />)

    await screen.findByText(/couldn.t load tasks/i)
    expect(screen.queryByText(/unprocessed · /)).not.toBeInTheDocument()
  })

  it('states that a duration write failed even when the follow-up reload also fails', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    let failEverythingAfterWrite = false
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        failEverythingAfterWrite = true
        return new Response(null, { status: 500 })
      }
      if (failEverythingAfterWrite) return new Response(null, { status: 500 })
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getByRole('button', { name: '2m' }))

    // Offline: the write fails AND the reload it triggers fails too, landing on the connection
    // error — the note must still say the write failed rather than being dropped by that arm.
    expect(await screen.findByText("Couldn't set this task's duration.")).toBeInTheDocument()
    expect(await screen.findByText(/couldn.t load tasks/i)).toBeInTheDocument()
  })

  it("a row's failure note is not cleared by a different row's duration button", async () => {
    unprocessed = [
      { id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' },
      { id: 'u2', title: 'Water plants', duration: null, createdAt: '2026-09-01T00:00:00Z' },
    ]
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        return new Response(null, { status: 500 })
      }
      if (init?.method === 'PUT' && url === '/api/tasks/u2/duration') {
        unprocessed = unprocessed.filter((t) => (t as { id: string }).id !== 'u2')
        return new Response(null, { status: 204 })
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getAllByRole('button', { name: '2m' })[0])
    await screen.findByText("Couldn't set this task's duration.")

    await user.click(screen.getAllByRole('button', { name: '2m' })[1])
    await waitFor(() => expect(screen.queryByText('Water plants')).not.toBeInTheDocument())
    expect(screen.getByText("Couldn't set this task's duration.")).toBeInTheDocument()
  })
})
