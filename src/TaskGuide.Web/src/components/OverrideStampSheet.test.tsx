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
// `GET /api/patterns` is the read that exists (PatternEndpoints.cs:21). `active` is NOT on
// PatternResponse yet — #143 — so `patterns(...)` below builds the shape that ticket will ship,
// and `noActiveFlag` builds today's, which must degrade to one ungrouped list.
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

// #140 review finding 1: the range scope used to offer a "Keep each date's own shape" row that
// called onStamp(null, span) — server-side that stamps a zero-window Override, blanking every
// date in the span, not the "detaches without changing what is on it" the copy promised. There is
// no per-date-preserving mode on the span endpoint (#144 tracks adding one), so the row is gone
// rather than relabelled, and no row in range scope may ever pass a null template.
it('range_scope_offers_no_row_that_can_send_a_null_template', async () => {
  stub(patterns([christmas.id]))
  const onStamp = vi.fn(async () => {})
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={onStamp} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  await screen.findByRole('button', { name: /Christmas/ })
  expect(screen.queryByText('Leave them as they are')).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Keep each date's own shape/ })).not.toBeInTheDocument()
  for (const button of screen.getAllByRole('button')) fireEvent.click(button)
  expect(onStamp).not.toHaveBeenCalledWith(null, expect.anything())
})

it('range_scope_passes_the_span_alongside_a_picked_template_id', async () => {
  stub(patterns([christmas.id]))
  const onStamp = vi.fn(async () => {})
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={onStamp} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-24' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-28' } })
  fireEvent.click(await screen.findByRole('button', { name: /Christmas/ }))
  await waitFor(() => expect(onStamp).toHaveBeenCalledWith(christmas.id, { from: '2026-12-24', to: '2026-12-28' }))
})

it('an_inverted_range_disables_every_pickrow_including_Keep_each_dates_own_shape_and_shows_the_refusal_note', async () => {
  stub(patterns([christmas.id]))
  render(<OverrideStampSheet date="2026-12-25" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  fireEvent.click(await screen.findByRole('button', { name: 'A range…' }))
  fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-12-28' } })
  fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-12-24' } })
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose a start and end date; the end must not precede the start.')
  expect(screen.getByRole('button', { name: /Christmas/ })).toBeDisabled()
})

// Today's actual wire shape. This is not a hypothetical: PatternResponse is (Id, Name, Days) with
// no active marker, so this is what the picker meets in the running app until #143 lands.
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
