import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { OverrideStampSheet } from './OverrideStampSheet'

function json(body: unknown) { return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } }) }
const win = (id: string, start: string, end: string) => ({ id: { value: id }, name: id, start, end, tags: { dimensions: {}, looseTags: [] } })
const christmas = { id: 'dt_christmas', name: 'Christmas', windows: [win('w1', '09:00:00', '10:00:00'), win('w2', '18:00:00', '19:00:00')], eventPrototypes: [], unused: false }
const spare = { id: 'dt_spare', name: 'Spare Sunday', windows: [], eventPrototypes: [], unused: true }
const attic = { id: 'dt_attic', name: 'Attic', windows: [win('w3', '08:00:00', '09:00:00')], eventPrototypes: [], unused: true }
const templates = [christmas, spare, attic]
let fetch: ReturnType<typeof vi.fn<(url: string) => Promise<Response>>>
// `GET /api/patterns` is the read that exists (PatternEndpoints.cs:21). `active` IS on
// PatternResponse (#143 landed; PatternEndpoints.cs:138), so `patterns(...)` is the real wire shape.
// `noActiveFlag` is no longer reachable over the wire — it is kept as a defensive case: the picker
// must still degrade to one ungrouped list rather than erroring if no Pattern comes back active.
function stub(body: unknown | 'fail') {
  fetch = vi.fn(async (url: string) => {
    if (url === '/api/day-templates') return json(templates)
    if (url === '/api/patterns') return body === 'fail' ? new Response(null, { status: 500 }) : json(body)
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetch)
}
const patterns = (days: string[]) => [
  { id: 'p0', name: 'Autumn', days: [attic.id], active: false },
  { id: 'p1', name: 'Winter', days, active: true },
]
const noActiveFlag = (days: string[]) => [{ id: 'p1', name: 'Winter', days }]
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

it('titles_the_sheet_with_the_short_date_not_a_generic_label', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  expect(await screen.findByRole('dialog', { name: 'Sun 1 Nov' })).toBeInTheDocument()
})

it('groups_templates_by_season_use_and_omits_an_empty_group', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  await screen.findByRole('button', { name: /Christmas/ })
  const headings = screen.getAllByText(/Already in this season|Used by other seasons|Not in use/)
  expect(headings.map(h => h.textContent?.replace(/Strips.*/, ''))).toEqual(['Already in this season', 'Not in use'])
  const inSeason = screen.getByText('Already in this season').closest('.sec-h')?.nextElementSibling
  expect(within(inSeason as HTMLElement).getByRole('button', { name: /Christmas/ })).toBeInTheDocument()
  const notInUse = screen.getByText('Not in use').closest('.sec-h')?.nextElementSibling
  expect(within(notInUse as HTMLElement).getByRole('button', { name: /Attic/ })).toBeInTheDocument()
  expect(screen.queryByText('Used by other seasons')).not.toBeInTheDocument()
})

it('only_the_first_group_heading_carries_with-tog_and_the_shared_toggle', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  await screen.findByRole('button', { name: /Christmas/ })
  const secHs = document.querySelectorAll('.sec-h')
  expect(secHs[0]).toHaveClass('with-tog')
  expect(within(secHs[0] as HTMLElement).getByRole('button', { name: 'Strips' })).toHaveAttribute('aria-pressed', 'true')
  expect(secHs[1]).not.toHaveClass('with-tog')
})

it('defaults_to_the_strip_view_and_switching_to_Times_replaces_the_strip_with_duration_pills_on_every_row', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Christmas/ })
  expect(shape.querySelector('.strip')).not.toBeNull()
  expect(shape.querySelector('.pill.dur')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Times' }))
  expect(shape.querySelector('.strip')).toBeNull()
  expect(shape.querySelectorAll('.pill.dur')).toHaveLength(2)
  expect(shape.querySelector('.pill.dur')).toHaveTextContent('9a–10a')
})

it('a_windowless_template_reads_no_windows_a_deliberately_silent_day_and_a_ghost_silent_pill_in_Times_view', async () => {
  stub(patterns([spare.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Spare Sunday/ })
  expect(shape.querySelector('.who > .sub2')).toHaveTextContent('no windows — a deliberately silent day')
  fireEvent.click(screen.getByRole('button', { name: 'Times' }))
  expect(shape.querySelector('.pill.ghost')).toHaveTextContent('silent')
})

// #140 review finding 3: a template's Windows compare as a multiset (DateOverride.cs: "a Window
// is a per-day instance, not a position"), and the server passes them through unsorted
// (OverrideEndpoints.cs:178). A template authored evening-first must not render an inverted span.
it('the_shape_summary_sorts_windows_before_taking_the_earliest_start_and_latest_end_even_when_authored_out_of_order', async () => {
  const eveningFirst = { id: 'dt_evening_first', name: 'Evening First', windows: [win('e1', '18:00:00', '19:00:00'), win('e2', '09:00:00', '10:00:00')], eventPrototypes: [], unused: false }
  stub(patterns([eveningFirst.id]))
  fetch.mockImplementation(async (url: string) => {
    if (url === '/api/day-templates') return json([eveningFirst])
    if (url === '/api/patterns') return json(patterns([eveningFirst.id]))
    return new Response(null, { status: 404 })
  })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Evening First/ })
  expect(shape.querySelector('.who > .sub2')).toHaveTextContent('2 windows · 9a–7p')
})

// The widest-span case: an overlapping window whose start is later must not steal `last` — the
// latest END wins, not the last-by-start element.
it('the_shape_summary_takes_the_latest_end_not_the_end_of_the_last-by-start_window', async () => {
  const overlapping = { id: 'dt_overlapping', name: 'Overlapping', windows: [win('o1', '09:00:00', '20:00:00'), win('o2', '10:00:00', '11:00:00')], eventPrototypes: [], unused: false }
  stub(patterns([overlapping.id]))
  fetch.mockImplementation(async (url: string) => {
    if (url === '/api/day-templates') return json([overlapping])
    if (url === '/api/patterns') return json(patterns([overlapping.id]))
    return new Response(null, { status: 404 })
  })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Overlapping/ })
  expect(shape.querySelector('.who > .sub2')).toHaveTextContent('2 windows · 9a–8p')
})

it('the_strip_positions_ticks_and_window_bars_from_the_6a-11p_span_as_inline_style_and_no_row_is_marked_current', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Christmas/ })
  expect(shape).toHaveAttribute('aria-pressed', 'false')
  expect(shape.querySelector('.who > .nm')?.querySelector('.pill.dim')).toBeNull()
  const ticks = shape.querySelectorAll('.strip u')
  expect(ticks).toHaveLength(5)
  // 9am tick: (9*60 - 360) / (23*60-360) * 100
  expect((ticks[0] as HTMLElement).style.left).toBe(`${(9 * 60 - 360) / 1020 * 100}%`)
  const bars = shape.querySelectorAll('.strip i')
  expect(bars).toHaveLength(2)
})

// #140 review finding 5: `left` was floored at 0 but never capped at 100, and `right` was capped
// at 100 but never floored at 0 — a window wholly past 11p or wholly before 6a rendered a bar
// outside the track (or, for the before-6a case, a phantom sliver at the very start of the
// track). Both ends must clamp to [0, 100] and a bar whose clamped width is zero must not render.
it('a_window_wholly_outside_the_6a-11p_band_renders_no_bar', async () => {
  const outOfBand = { id: 'dt_out_of_band', name: 'Out Of Band', windows: [win('late', '23:15:00', '23:45:00'), win('early', '04:00:00', '05:00:00')], eventPrototypes: [], unused: false }
  stub(patterns([outOfBand.id]))
  fetch.mockImplementation(async (url: string) => {
    if (url === '/api/day-templates') return json([outOfBand])
    if (url === '/api/patterns') return json(patterns([outOfBand.id]))
    return new Response(null, { status: 404 })
  })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Out Of Band/ })
  expect(shape.querySelectorAll('.strip i')).toHaveLength(0)
})

it('a_window_crossing_the_11p_edge_clamps_its_bar_to_the_track_rather_than_spilling_past_it', async () => {
  const crossing = { id: 'dt_crossing', name: 'Crossing', windows: [win('c1', '22:00:00', '23:30:00')], eventPrototypes: [], unused: false }
  stub(patterns([crossing.id]))
  fetch.mockImplementation(async (url: string) => {
    if (url === '/api/day-templates') return json([crossing])
    if (url === '/api/patterns') return json(patterns([crossing.id]))
    return new Response(null, { status: 404 })
  })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Crossing/ })
  const bar = shape.querySelector('.strip i') as HTMLElement
  expect(bar).not.toBeNull()
  expect(parseFloat(bar.style.left)).toBeLessThanOrEqual(100)
  expect(parseFloat(bar.style.left) + parseFloat(bar.style.width)).toBeLessThanOrEqual(100.001)
})

it('a_genuinely_tiny_in-band_window_still_renders_at_the_1_2_percent_floor', async () => {
  const tiny = { id: 'dt_tiny', name: 'Tiny', windows: [win('t1', '08:00:00', '08:03:00')], eventPrototypes: [], unused: false }
  stub(patterns([tiny.id]))
  fetch.mockImplementation(async (url: string) => {
    if (url === '/api/day-templates') return json([tiny])
    if (url === '/api/patterns') return json(patterns([tiny.id]))
    return new Response(null, { status: 404 })
  })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Tiny/ })
  const bar = shape.querySelector('.strip i') as HTMLElement
  expect(bar).not.toBeNull()
  expect(bar.style.width).toBe('1.2%')
})

it('when_the_active_pattern_cannot_be_read_it_falls_back_to_one_ungrouped_list_instead_of_an_error', async () => {
  stub('fail')
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  await screen.findByRole('button', { name: /Christmas/ })
  expect(screen.queryByText('Already in this season')).not.toBeInTheDocument()
  expect(screen.queryByText('Not in use')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Attic/ })).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('closes_with_the_shape_count_and_grouping-rationale_note_alongside_the_existing_not-a-link_note', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const sheet = await screen.findByRole('dialog')
  expect(sheet).toHaveTextContent('3 shapes. Grouping by use keeps the one you want near the top')
  expect(sheet).toHaveTextContent('not a link')
})

it('defaults_to_This_date_scope_with_the_single-date_title_and_note_and_switching_to_A_range_reveals_From_To_and_switches_the_title_and_note', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  expect(await screen.findByRole('dialog', { name: 'Sun 1 Nov' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'This date' })).toHaveAttribute('aria-pressed', 'true')
  expect(screen.getByRole('button', { name: 'A range…' })).toHaveAttribute('aria-pressed', 'false')
  expect(screen.queryByLabelText('From')).not.toBeInTheDocument()
  expect(screen.getByRole('dialog')).toHaveTextContent('Stamp a shape onto this date.')
  fireEvent.click(screen.getByRole('button', { name: 'A range…' }))
  expect(screen.getByRole('button', { name: 'This date' })).toHaveAttribute('aria-pressed', 'false')
  expect(screen.getByRole('button', { name: 'A range…' })).toHaveAttribute('aria-pressed', 'true')
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  expect(await screen.findByRole('dialog', { name: 'Thu 24 Dec – Mon 28 Dec' })).toBeInTheDocument()
  expect(screen.getByRole('dialog')).toHaveTextContent('Stamp a shape onto every date in the span.')
})

// In range scope the sheet offers three arms and only one of them stamps, so the stamp note cannot
// sit at the top as the sheet's own instruction — there it claims "Stamp a shape onto every date in
// the span" directly above two rows that do no such thing. It belongs below them, captioning the
// shape list it actually describes. Date scope renders neither non-template row, so nothing moves.
it('the_stamp_note_follows_the_freeze_and_blank_rows_rather_than_heading_the_whole_sheet', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  const note = await screen.findByText(/Stamp a shape onto every date in the span/)
  const blankRow = screen.getByRole('button', { name: /Blank every date in the span/ })
  const shapeRow = await screen.findByRole('button', { name: /Christmas/ })
  // DOCUMENT_POSITION_FOLLOWING === 4: the note comes after the blank row and before the shapes.
  expect(blankRow.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  expect(note.compareDocumentPosition(shapeRow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
})

// #140 review finding 1, closed by #145: the range scope used to offer a "Keep each date's own
// shape" row that called onStamp(null, span) — server-side that stamped a zero-window Override,
// blanking every date in the span, not the "detaches without changing what is on it" the copy
// promised. #145 gave the wire a real, non-destructive freeze arm (mode: 'freeze'), so the row is
// back with wording that now matches what the write actually does, and every row states its mode
// explicitly — no row ever again relies on a null-template default.
it('the_range_scope_offers_a_blank_every_date_row_whose_wording_matches_what_the_write_actually_does', async () => {
  stub(patterns([christmas.id]))
  const onStamp = vi.fn(async () => {})
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={onStamp} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  await screen.findByRole('button', { name: /Christmas/ })

  const row = screen.getByRole('button', { name: /Blank every date in the span/ })
  expect(row).toHaveTextContent('every window on those dates is removed')
  expect(row).not.toHaveTextContent('nothing will fire')
  expect(row.querySelector('.pill.due')).toHaveTextContent('destructive')
  expect(screen.getByText('Clear the span')).toBeInTheDocument()

  fireEvent.click(row)
  expect(onStamp).toHaveBeenCalledWith(null, { from: '2026-12-24', to: '2026-12-28' }, 'blank')
})

it('the_range_scope_also_offers_a_freeze_row_above_the_blank_row_distinct_and_undestructive', async () => {
  stub(patterns([christmas.id]))
  const onStamp = vi.fn(async () => {})
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={onStamp} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  await screen.findByRole('button', { name: /Christmas/ })

  expect(screen.getByText('Detach the span')).toBeInTheDocument()
  const freezeRow = screen.getByRole('button', { name: /Keep each date's own shape/ })
  expect(freezeRow.querySelector('.pill.due')).toBeNull()
  const blankRow = screen.getByRole('button', { name: /Blank every date in the span/ })
  expect(freezeRow).not.toBe(blankRow)

  fireEvent.click(freezeRow)
  expect(onStamp).toHaveBeenCalledWith(null, { from: '2026-12-24', to: '2026-12-28' }, 'freeze')
})

it('the_blank_and_freeze_rows_are_offered_only_in_range_scope', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  await screen.findByRole('button', { name: /Christmas/ })
  expect(screen.queryByRole('button', { name: /Blank every date/ })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Keep each date's own shape/ })).not.toBeInTheDocument()
})

it('range_scope_passes_the_span_and_stamp_mode_alongside_a_picked_template_id', async () => {
  stub(patterns([christmas.id]))
  const onStamp = vi.fn(async () => {})
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={onStamp} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  fireEvent.click(await screen.findByRole('button', { name: /Christmas/ }))
  await waitFor(() => expect(onStamp).toHaveBeenCalledWith(christmas.id, { from: '2026-12-24', to: '2026-12-28' }, 'stamp'))
})

it('an_inverted_range_disables_every_pickrow_including_Keep_each_dates_own_shape_and_shows_the_refusal_note', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-12-25" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-28' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-24' } })
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose a start and end date; the end must not precede the start.')
  expect(screen.getByRole('button', { name: /Christmas/ })).toBeDisabled()
  // The name says "every pickrow", so assert the two non-template rows too — without these, dropping
  // `disabled={rowsDisabled}` from either would pass here and let a click reach authorOverrideSpan,
  // surfacing its raw "End date must not precede start date" throw instead of the sheet's refusal.
  expect(screen.getByRole('button', { name: /Keep each date's own shape/ })).toBeDisabled()
  expect(screen.getByRole('button', { name: /Blank every date in the span/ })).toBeDisabled()
})

// Not the wire shape any more (#143 shipped `active`), but the picker must not error when no
// Pattern comes back marked active — an empty store or a season yet to be chosen reads this way.
it('a_patterns_read_with_no_active_marker_degrades_to_one_ungrouped_list_rather_than_an_error', async () => {
  stub(noActiveFlag([christmas.id]))
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  expect(await screen.findByRole('dialog', { name: 'Sun 1 Nov' })).toBeInTheDocument()
  await waitFor(() => expect(screen.getAllByRole('button', { name: /Christmas|Spare Sunday|Attic/ })).toHaveLength(3))
  expect(screen.queryByText('Already in this season')).toBeNull()
  expect(screen.queryByText('Used by other seasons')).toBeNull()
  expect(screen.queryByText('Not in use')).toBeNull()
  expect(screen.queryByRole('alert')).toBeNull()
})
