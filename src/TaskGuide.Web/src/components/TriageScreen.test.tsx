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
      expect(screen.getByRole('button', { name: `${label} — File the receipt` })).toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))

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

  it('an empty pile renders its own empty-state line rather than being hidden', async () => {
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)
    await screen.findByText('Missing a duration')

    expect(screen.getByText('Nothing to process.')).toBeInTheDocument()
    expect(screen.getByText('Nothing stale.')).toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))

    expect(await screen.findByText('Couldn\'t set the duration for "File the receipt".')).toBeInTheDocument()
    // Untouched by the refused write — still in the unprocessed pile, buttons re-enabled.
    expect(screen.getByText('File the receipt')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '2m — File the receipt' })).not.toBeDisabled()
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
    await screen.findByText('Nothing to process.')
    expect(screen.getByText('Nothing stale.')).toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))

    // Offline: the write fails AND the reload it triggers fails too, landing on the connection
    // error — the note must still say the write failed rather than being dropped by that arm.
    expect(await screen.findByText('Couldn\'t set the duration for "File the receipt".')).toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))
    await screen.findByText('Couldn\'t set the duration for "File the receipt".')

    await user.click(screen.getByRole('button', { name: '2m — Water plants' }))
    await waitFor(() => expect(screen.queryByText('Water plants')).not.toBeInTheDocument())
    expect(screen.getByText('Couldn\'t set the duration for "File the receipt".')).toBeInTheDocument()
  })

  it('a_refused_Duration_write_renders_the_servers_reason', async () => {
    unprocessed = [{ id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' }]
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        return jsonResponse({ error: 'A derived Task cannot have its Duration edited' }, { status: 409 })
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))

    expect(await screen.findByText('A derived Task cannot have its Duration edited')).toBeInTheDocument()
    expect(screen.getByText('File the receipt')).toBeInTheDocument()
  })

  it('the duration-write failure note is announced as an alert', async () => {
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

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Couldn\'t set the duration for "File the receipt".')
  })

  it("each unprocessed row's Duration buttons are labelled with the task they size, not just the bucket", async () => {
    unprocessed = [
      { id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' },
      { id: 'u2', title: 'Water plants', duration: null, createdAt: '2026-09-01T00:00:00Z' },
    ]
    vi.stubGlobal('fetch', vi.fn(routedFetch))

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    // With two rows both offering "2m", a screen-reader user tabbing the pile can't tell them
    // apart without the task in the accessible name.
    expect(screen.getByRole('button', { name: '2m — File the receipt' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '2m — Water plants' })).toBeInTheDocument()
  })

  it("a second row's in-flight write does not re-enable a first row's still-in-flight controls", async () => {
    unprocessed = [
      { id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' },
      { id: 'u2', title: 'Water plants', duration: null, createdAt: '2026-09-01T00:00:00Z' },
    ]
    const pending: Record<string, (r: Response) => void> = {}
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        return new Promise<Response>((res) => {
          pending[url] = res
        })
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))
    expect(screen.getByRole('button', { name: '2m — File the receipt' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: '10m — Water plants' }))
    // Row 1's write is still in flight — a single busy-id slot would have dropped its guard the
    // moment row 2 became "the" busy row, re-enabling a control whose write hadn't settled yet.
    expect(screen.getByRole('button', { name: '2m — File the receipt' })).toBeDisabled()

    pending['/api/tasks/u1/duration'](new Response(null, { status: 204 }))
    pending['/api/tasks/u2/duration'](new Response(null, { status: 204 }))
  })

  it("a slower reload from an earlier write does not overwrite a newer write's fresher pile", async () => {
    unprocessed = [
      { id: 'u1', title: 'File the receipt', duration: null, createdAt: '2026-09-01T00:00:00Z' },
      { id: 'u2', title: 'Water plants', duration: null, createdAt: '2026-09-01T00:00:00Z' },
    ]
    let unprocessedReads = 0
    let releaseStaleReload: (() => void) | null = null
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url === '/api/tasks/u1/duration') {
        unprocessed = unprocessed.filter((t) => (t as { id: string }).id !== 'u1')
        return new Response(null, { status: 204 })
      }
      if (init?.method === 'PUT' && url === '/api/tasks/u2/duration') {
        unprocessed = unprocessed.filter((t) => (t as { id: string }).id !== 'u2')
        return new Response(null, { status: 204 })
      }
      if (url === '/api/tasks?status=unprocessed') {
        unprocessedReads += 1
        // Read #1 is the initial mount load. Read #2 is u1's post-write reload — hold it open,
        // capturing today's snapshot (u2 still present) now, so it resolves later with stale data
        // once released below, arriving after u2's own fresher reload (#3).
        if (unprocessedReads === 2) {
          const snapshot = [...unprocessed]
          return new Promise<Response>((resolve) => {
            releaseStaleReload = () => resolve(jsonResponse(snapshot))
          })
        }
        return jsonResponse(unprocessed)
      }
      return routedFetch(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<TriageScreen />)
    await screen.findByText('File the receipt')

    await user.click(screen.getByRole('button', { name: '2m — File the receipt' }))
    await waitFor(() => expect(unprocessedReads).toBe(2))

    await user.click(screen.getByRole('button', { name: '10m — Water plants' }))
    await waitFor(() => expect(screen.queryByText('Water plants')).not.toBeInTheDocument())

    // Release the held, now-stale reload from u1's write. The sequence guard must drop it rather
    // than resurrect u2, which it still lists.
    releaseStaleReload?.()
    await waitFor(() => expect(unprocessedReads).toBe(3))
    expect(screen.queryByText('Water plants')).not.toBeInTheDocument()
  })
})
