import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EventCreateSheet, type EventWindow } from './EventCreateSheet'

const windows: EventWindow[] = [
  { id: { value: 'morning' }, name: 'Morning', start: '09:00', end: '12:00', tags: { dimensions: {}, looseTags: [] } },
  { id: { value: 'afternoon' }, name: 'Afternoon', start: '13:00', end: '16:00', tags: { dimensions: {}, looseTags: [] } },
]

function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
}

beforeEach(() => vi.restoreAllMocks())

describe('EventCreateSheet', () => {
  it('an event overlapping no window renders the add button and the nothing-to-resolve note, and no resolution list at all', () => {
    render(<EventCreateSheet date="2026-09-07" windows={windows} onCancel={() => {}} onCreated={() => {}} />)

    expect(screen.getByRole('button', { name: 'Add event' })).toBeInTheDocument()
    expect(screen.getByText(/nothing to resolve/i)).toBeInTheDocument()
    expect(screen.queryByText(/replace the window/i)).not.toBeInTheDocument()
  })

  it('every overlapping window gets its own resolution, and they are sent together in the POST /api/events body', async () => {
    const user = userEvent.setup()
    const onCreated = vi.fn()
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: { value: 'event-1' } }))
    vi.stubGlobal('fetch', fetchMock)

    render(<EventCreateSheet date="2026-09-07" windows={windows} onCancel={() => {}} onCreated={onCreated} />)
    await user.type(screen.getByLabelText('Name'), 'School pickup')
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '08:30')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '14:00')

    expect(screen.getByText(/Morning, 09:00–12:00/i)).toBeInTheDocument()
    expect(screen.getByText(/Afternoon, 13:00–16:00/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /replace the window.*morning/i }))
    await user.click(screen.getByRole('button', { name: /push it to after the event.*afternoon/i }))
    await user.click(screen.getByRole('button', { name: 'Add event' }))

    expect(fetchMock).toHaveBeenCalledWith('/api/events', expect.objectContaining({ method: 'POST' }))
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toMatchObject({
      date: '2026-09-07', name: 'School pickup', start: '08:30', end: '14:00',
      resolutions: [
        { windowId: 'morning', resolution: 'replace' },
        { windowId: 'afternoon', resolution: 'truncateStart' },
      ],
    })
    expect(onCreated).toHaveBeenCalledOnce()
  })

  it('the resolution set is closed at the four wire values, and no option whose guard is false is ever rendered', async () => {
    const user = userEvent.setup()
    render(<EventCreateSheet date="2026-09-07" windows={[windows[0]]} onCancel={() => {}} onCreated={() => {}} />)

    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '10:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '11:00')

    expect(screen.getByRole('button', { name: /replace the window/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /truncate it to 09:00–10:00/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /split it around the event/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /push it to after the event/i })).not.toBeInTheDocument()

    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '08:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '10:00')

    expect(screen.getByRole('button', { name: /push it to after the event/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /truncate it to/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /split it around the event/i })).not.toBeInTheDocument()
  })

  it("an event's time fields refuse an end at or before the start, and survive their own input events", () => {
    render(<EventCreateSheet date="2026-09-07" windows={[]} onCancel={() => {}} onCreated={() => {}} />)
    const end = screen.getByLabelText('End')

    fireEvent.change(end, { target: { value: '06:00' } })

    expect(screen.getByText(/end time must be after/i)).toBeInTheDocument()
    expect(screen.getByLabelText('End')).toBe(end)
    expect(screen.getByRole('button', { name: 'Add event' })).toBeDisabled()
  })
})
