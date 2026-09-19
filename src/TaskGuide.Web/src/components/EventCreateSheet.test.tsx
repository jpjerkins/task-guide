import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EventCreateSheet, type EventWindow } from './EventCreateSheet'
import { fmtShort } from './OverrideFormat'

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
    const { container } = render(<EventCreateSheet date="2026-09-07" windows={windows} onCancel={() => {}} onCreated={() => {}} />)

    expect(screen.getByRole('button', { name: 'Add event' })).toBeInTheDocument()
    expect(screen.getByText(/nothing to resolve/i)).toBeInTheDocument()
    expect(screen.queryByText(/replace the window/i)).not.toBeInTheDocument()
    expect(container.querySelector('.veil > .sheet')).toBeInTheDocument()
    expect(container.querySelectorAll('input.field.time')).toHaveLength(2)
    expect(screen.getByText('to')).toBeInTheDocument()
    expect(container.querySelector('.btn-row > .btn.primary.wide')).toBeInTheDocument()
    expect(container.querySelector('.note')).toBeInTheDocument()
    expect(screen.getByLabelText('Start')).toHaveValue('6a')
    expect(screen.getByLabelText('End')).toHaveValue('7a')
    expect(screen.getByText(/type it how you say it/i)).toBeInTheDocument()
    expect(screen.getByText(new RegExp(`When — ${fmtShort('2026-09-07')}`))).toBeInTheDocument()
  })

  it('every overlapping window gets its own resolution, and they are sent together in the POST /api/events body', async () => {
    const user = userEvent.setup()
    const onCreated = vi.fn()
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: { value: 'event-1' } }))
    vi.stubGlobal('fetch', fetchMock)

    const { container } = render(<EventCreateSheet date="2026-09-07" windows={windows} onCancel={() => {}} onCreated={onCreated} />)
    await user.type(screen.getByLabelText('Name'), 'School pickup')
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '08:30')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '14:00')

    const clash = container.querySelector('.scope.shared')
    expect(clash).toHaveTextContent('Morning, 9a–12p')
    expect(clash).toHaveTextContent('Afternoon')
    expect(container.querySelector('.scope.shared .g')).toHaveTextContent('⚠')
    expect(screen.getByText(/Morning disappears that day/i)).toBeInTheDocument()
    expect(screen.getByText(/the event covers its whole start/i)).toBeInTheDocument()

    const secHeaders = container.querySelectorAll('.sec-h')
    expect(secHeaders).toHaveLength(2)
    expect(secHeaders[0]).toHaveTextContent('Morning')
    expect(secHeaders[1]).toHaveTextContent('Afternoon')

    // #140 review finding 6: the split clause used to render unconditionally. Here the first
    // overlap is Morning, whose options are Replace only (the event starts before the window, so
    // optionsFor never offers split or truncateEnd) — the note must not mention a split nobody
    // was offered.
    expect(screen.queryByText(/move the end time to .* or later/i)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /replace the window.*morning disappears/i }))
    await user.click(screen.getByRole('button', { name: /push it to after the event.*covers its whole start/i }))
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

  it('an_event_starting_after_the_window_offers_no_split_and_the_overlap_note_omits_the_split_clause', async () => {
    const user = userEvent.setup()
    const window = [{ id: { value: 'w1' }, name: 'Window', start: '06:00', end: '10:00', tags: { dimensions: {}, looseTags: [] } }]
    render(<EventCreateSheet date="2026-09-07" windows={window} onCancel={() => {}} onCreated={() => {}} />)
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '05:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '08:00')

    expect(screen.getByRole('button', { name: /replace the window/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /push it to after the event/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /split it around the event/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/move the end time to .* or later/i)).not.toBeInTheDocument()
  })

  it('the_overlap_note_keeps_the_split_clause_when_split_is_actually_one_of_the_offered_options', async () => {
    const user = userEvent.setup()
    render(<EventCreateSheet date="2026-09-07" windows={[windows[0]]} onCancel={() => {}} onCreated={() => {}} />)
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '10:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '11:00')

    expect(screen.getByRole('button', { name: /split it around the event/i })).toBeInTheDocument()
    expect(screen.getByText(/move the end time to 12p or later/i)).toBeInTheDocument()
  })

  it('the resolution set is closed at the four wire values, and no option whose guard is false is ever rendered', async () => {
    const user = userEvent.setup()
    render(<EventCreateSheet date="2026-09-07" windows={[windows[0]]} onCancel={() => {}} onCreated={() => {}} />)

    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '10:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '11:00')

    expect(screen.getByRole('button', { name: /replace the window/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /truncate it to 9a–10a/i })).toBeInTheDocument()
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

  it('changing the times after a selection blocks submission when that resolution is no longer offered', async () => {
    const user = userEvent.setup()
    render(<EventCreateSheet date="2026-09-07" windows={[windows[0]]} onCancel={() => {}} onCreated={() => {}} />)
    await user.type(screen.getByLabelText('Name'), 'School pickup')
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '10:00')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '11:00')
    await user.click(screen.getByRole('button', { name: /split it around the event/i }))

    expect(screen.getByRole('button', { name: 'Add event' })).toBeEnabled()

    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '12:00')

    expect(screen.queryByRole('button', { name: /split it around the event/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add event' })).toBeDisabled()
  })

  it("an event's time fields accept the same lenient forms as a window and send canonical times", async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: { value: 'event-1' } }))
    vi.stubGlobal('fetch', fetchMock)
    render(<EventCreateSheet date="2026-09-07" windows={[]} onCancel={() => {}} onCreated={() => {}} />)
    await user.type(screen.getByLabelText('Name'), 'Breakfast')
    await user.clear(screen.getByLabelText('Start'))
    await user.type(screen.getByLabelText('Start'), '6')
    await user.clear(screen.getByLabelText('End'))
    await user.type(screen.getByLabelText('End'), '630')

    await user.click(screen.getByRole('button', { name: 'Add event' }))

    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toMatchObject({ start: '06:00', end: '06:30' })
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
