import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { OverrideStampSheet } from './OverrideStampSheet'

function json(body: unknown) { return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } }) }
const win = (id: string, start: string, end: string) => ({ id: { value: id }, name: id, start, end, tags: { dimensions: {}, looseTags: [] } })
const christmas = { id: 'dt_christmas', name: 'Christmas', windows: [win('w1', '09:00:00', '10:00:00'), win('w2', '18:00:00', '19:00:00')], eventPrototypes: [], unused: false }
const spare = { id: 'dt_spare', name: 'Spare Sunday', windows: [], eventPrototypes: [], unused: true }
const attic = { id: 'dt_attic', name: 'Attic', windows: [win('w3', '08:00:00', '09:00:00')], eventPrototypes: [], unused: true }
const templates = [christmas, spare, attic]
let fetch: ReturnType<typeof vi.fn<(url: string) => Promise<Response>>>
function stub(activePattern: unknown | 'fail') {
  fetch = vi.fn(async (url: string) => {
    if (url === '/api/day-templates') return json(templates)
    if (url === '/api/patterns/active') return activePattern === 'fail' ? new Response(null, { status: 404 }) : json(activePattern)
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetch)
}
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

it('titles_the_sheet_with_the_short_date_not_a_generic_label', async () => {
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  expect(await screen.findByRole('dialog', { name: 'Sun 1 Nov' })).toBeInTheDocument()
})

it('groups_templates_by_season_use_and_omits_an_empty_group', async () => {
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
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
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  await screen.findByRole('button', { name: /Christmas/ })
  const secHs = document.querySelectorAll('.sec-h')
  expect(secHs[0]).toHaveClass('with-tog')
  expect(within(secHs[0] as HTMLElement).getByRole('button', { name: 'Strips' })).toHaveAttribute('aria-pressed', 'true')
  expect(secHs[1]).not.toHaveClass('with-tog')
})

it('defaults_to_the_strip_view_and_switching_to_Times_replaces_the_strip_with_duration_pills_on_every_row', async () => {
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
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
  stub({ id: 'p1', name: 'Winter', days: [spare.id] })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const shape = await screen.findByRole('button', { name: /Spare Sunday/ })
  expect(shape.querySelector('.who > .sub2')).toHaveTextContent('no windows — a deliberately silent day')
  fireEvent.click(screen.getByRole('button', { name: 'Times' }))
  expect(shape.querySelector('.pill.ghost')).toHaveTextContent('silent')
})

it('the_strip_positions_ticks_and_window_bars_from_the_6a-11p_span_as_inline_style_and_no_row_is_marked_current', async () => {
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
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
  stub({ id: 'p1', name: 'Winter', days: [christmas.id] })
  render(<OverrideStampSheet date="2026-11-01" onCancel={() => {}} onStamp={async () => {}} busy={false} />)
  const sheet = await screen.findByRole('dialog')
  expect(sheet).toHaveTextContent('3 shapes. Grouping by use keeps the one you want near the top')
  expect(sheet).toHaveTextContent('not a link')
})
