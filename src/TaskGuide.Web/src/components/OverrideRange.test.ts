import { afterEach, expect, it, vi } from 'vitest'
import type { components } from '../api/schema'
import { authorOverrideSpan } from './OverrideRange'

function json(body: unknown) {
  return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
}

afterEach(() => vi.unstubAllGlobals())

it('a_range_landing_on_dates_that_already_carry_an_Override_names_every_one_of_them_in_a_single_confirmation_before_the_write_not_one_prompt_per_date', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(['2026-12-24', '2026-12-25', '2026-12-27']))
  vi.stubGlobal('fetch', fetch)
  const confirm = vi.fn(async (dates: readonly string[], span: number, mode: string) => {
    expect(dates).toEqual(['2026-12-24', '2026-12-25', '2026-12-27'])
    expect(span).toBe(5)
    expect(mode).toBe('stamp')
    expect(fetch).toHaveBeenCalledExactlyOnceWith('/api/overrides/clobber-check?from=2026-12-24&to=2026-12-28')
    return false
  })

  await expect(authorOverrideSpan({ from: '2026-12-24', to: '2026-12-28', templateId: 'dt_christmas', mode: 'stamp' }, confirm)).resolves.toBeNull()
  expect(confirm).toHaveBeenCalledOnce()
  expect(fetch).toHaveBeenCalledTimes(1)
})

it('range_authoring_takes_a_start_and_an_end_and_writes_one_Override_per_date_in_the_range_the_same_stamp_repeated_never_a_second_mechanism_and_never_a_multi_day_object', async () => {
  // The server owns the COPY operation; its per-date windows are the result, not a template link.
  const copiedWindow = { id: { value: 'w_original' }, name: 'Christmas dinner', start: '18:00:00', end: '19:00:00', tags: { dimensions: {}, looseTags: [] } }
  const days: components['schemas']['DateOverrideResponse'][] = [
    { date: '2026-12-24', windows: [copiedWindow], used: { templateId: 'dt_christmas', templateName: 'Christmas' } },
    { date: '2026-12-25', windows: [copiedWindow], used: { templateId: 'dt_christmas', templateName: 'Christmas' } },
  ]
  const fetch = vi.fn().mockResolvedValueOnce(json(['2026-12-24'])).mockResolvedValueOnce(json(days))
  vi.stubGlobal('fetch', fetch)
  let answer: (confirmed: boolean) => void = () => {}
  const confirm = vi.fn(() => new Promise<boolean>((resolve) => { answer = resolve }))
  const span = { from: '2026-12-24', to: '2026-12-25', templateId: 'dt_christmas', mode: 'stamp' as const }
  const pending = authorOverrideSpan(span, confirm)
  await vi.waitFor(() => expect(confirm).toHaveBeenCalledOnce())
  expect(fetch).toHaveBeenCalledTimes(1)
  answer(true)

  await expect(pending).resolves.toEqual(days)
  expect(fetch).toHaveBeenCalledTimes(2)
  expect(fetch).toHaveBeenLastCalledWith('/api/overrides', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(span),
  })
})

it('a_range_whose_end_precedes_its_start_is_refused_and_nothing_is_written', async () => {
  const fetch = vi.fn().mockImplementation(async () => json([]))
  vi.stubGlobal('fetch', fetch)
  const confirm = vi.fn()
  await expect(authorOverrideSpan({ from: '2026-12-25', to: '2026-12-24', templateId: null, mode: 'blank' }, confirm)).rejects.toThrow('End date must not precede start date')
  expect(fetch).not.toHaveBeenCalled()
  expect(confirm).not.toHaveBeenCalled()
})

it('a_single_date_span_uses_the_same_check_and_span_POST_and_needs_no_confirmation_when_nothing_is_clobbered', async () => {
  const day = { date: '2026-12-25', windows: [], used: null }
  const fetch = vi.fn().mockResolvedValueOnce(json([])).mockResolvedValueOnce(json([day]))
  vi.stubGlobal('fetch', fetch)
  const confirm = vi.fn()
  const span = { from: '2026-12-25', to: '2026-12-25', templateId: null, mode: 'blank' as const }
  await expect(authorOverrideSpan(span, confirm)).resolves.toEqual([day])
  expect(fetch.mock.calls).toEqual([
    ['/api/overrides/clobber-check?from=2026-12-25&to=2026-12-25'],
    ['/api/overrides', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(span) }],
  ])
  expect(confirm).not.toHaveBeenCalled()
})

it.each(['', '2026-02-30', '2026-13-01', '2026-1-01', '0000-01-01'])('an_incomplete_or_invalid_calendar_date_is_refused_before_checking_or_writing_%s', async (date) => {
  const fetch = vi.fn().mockImplementation(async () => json([]))
  vi.stubGlobal('fetch', fetch)
  await expect(authorOverrideSpan({ from: date, to: date, templateId: null, mode: 'blank' }, vi.fn())).rejects.toThrow('Valid start and end dates are required')
  expect(fetch).not.toHaveBeenCalled()
})

it('confirmation_applies_only_to_the_span_and_template_that_were_checked', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(['2026-12-25'])).mockResolvedValueOnce(json([]))
  vi.stubGlobal('fetch', fetch)
  const span = { from: '2026-12-25', to: '2026-12-25', templateId: 'dt_christmas', mode: 'stamp' as const }
  const original = { ...span }
  await authorOverrideSpan(span, async () => {
    span.to = '2026-12-31'
    span.templateId = 'dt_other'
    return true
  })
  expect(fetch).toHaveBeenLastCalledWith('/api/overrides', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(original),
  })
})

it('an_absent_clobber_check_response_is_not_permission_to_write', async () => {
  const fetch = vi.fn().mockImplementation(async () => new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetch)
  await expect(authorOverrideSpan({ from: '2026-12-25', to: '2026-12-25', templateId: null, mode: 'blank' }, vi.fn())).rejects.toThrow('Clobber check returned no dates response')
  expect(fetch).toHaveBeenCalledTimes(1)
})

it.each([400, 500])('a_failed_clobber_check_surfaces_the_error_without_confirmation_or_write_%s', async (status) => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status }))
  vi.stubGlobal('fetch', fetch)
  const confirm = vi.fn()
  await expect(authorOverrideSpan({ from: '2026-12-25', to: '2026-12-25', templateId: null, mode: 'blank' }, confirm)).rejects.toThrow(`failed: ${status}`)
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(confirm).not.toHaveBeenCalled()
})

it('a_failed_span_POST_surfaces_the_error_without_retrying_individual_dates', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json([])).mockResolvedValueOnce(new Response(null, { status: 409 }))
  vi.stubGlobal('fetch', fetch)
  await expect(authorOverrideSpan({ from: '2026-12-24', to: '2026-12-25', templateId: null, mode: 'blank' }, vi.fn())).rejects.toThrow('POST /api/overrides failed: 409')
  expect(fetch).toHaveBeenCalledTimes(2)
})

it('freeze_mode_skips_the_clobber_check_entirely_and_posts_directly_with_no_confirmation_even_when_dates_in_the_span_already_carry_Overrides', async () => {
  const days = [{ date: '2026-12-24', windows: [], used: { templateId: 'dt_christmas', templateName: 'Christmas' } }]
  const fetch = vi.fn().mockResolvedValueOnce(json(days))
  vi.stubGlobal('fetch', fetch)
  const confirm = vi.fn()
  const span = { from: '2026-12-24', to: '2026-12-25', templateId: null, mode: 'freeze' as const }
  await expect(authorOverrideSpan(span, confirm)).resolves.toEqual(days)
  expect(fetch).toHaveBeenCalledExactlyOnceWith('/api/overrides', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(span),
  })
  expect(confirm).not.toHaveBeenCalled()
})
