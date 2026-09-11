import { getJson, sendJson } from '../api/client'
import type { components, paths } from '../api/schema'

export type OverrideSpan = components['schemas']['OverrideSpanApiRequest']
type CreatedDays = components['schemas']['DateOverrideResponse'][]
type AffectedDates = paths['/api/overrides/clobber-check']['get']['responses'][200]['content']['application/json']

// #107: the server checks clobbers and performs the stamp COPY across the span. The caller
// presents all dates and resolves confirmation; this module owns no confirmation markup.
// null means cancellation (or the shared client's absent POST response); failures propagate.
export async function authorOverrideSpan(
  request: OverrideSpan,
  confirm: (dates: Readonly<AffectedDates>) => Promise<boolean>,
): Promise<CreatedDays | null> {
  const span = { ...request }
  if (![span.from, span.to].every(isCalendarDate)) throw new Error('Valid start and end dates are required')
  if (span.to < span.from) throw new Error('End date must not precede start date')
  const dates = await getJson<AffectedDates>(`/api/overrides/clobber-check?from=${span.from}&to=${span.to}`)
  if (dates === null) throw new Error('Clobber check returned no dates response')
  if (dates.length && !await confirm(dates)) return null
  return sendJson<CreatedDays>('POST', '/api/overrides', span)
}

function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false
  const date = new Date(`${value}T00:00:00Z`)
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
}
