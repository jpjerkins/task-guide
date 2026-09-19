import { getJson, sendJson } from '../api/client'
import type { components, paths } from '../api/schema'

// mode narrowed to a literal union and made required — #145 stopped leaning on the wire's loose,
// optional `mode?: null | string` (whose null used to mean "blank" by default).
export type OverrideSpan = Omit<components['schemas']['OverrideSpanApiRequest'], 'mode'> & { mode: 'stamp' | 'freeze' | 'blank' }
type CreatedDays = components['schemas']['DateOverrideResponse'][]
type AffectedDates = paths['/api/overrides/clobber-check']['get']['responses'][200]['content']['application/json']

// #107: the server checks clobbers and performs the stamp COPY across the span. The caller
// presents all dates and resolves confirmation; this module owns no confirmation markup.
// null means cancellation (or the shared client's absent POST response); failures propagate.
export async function authorOverrideSpan(
  request: OverrideSpan,
  confirm: (dates: Readonly<AffectedDates>, span: number, mode: Exclude<OverrideSpan['mode'], 'freeze'>) => Promise<boolean>,
): Promise<CreatedDays | null> {
  const span = { ...request }
  if (![span.from, span.to].every(isCalendarDate)) throw new Error('Valid start and end dates are required')
  if (span.to < span.from) throw new Error('End date must not precede start date')
  // Freeze copies each date's own computed shape into its own Override (CreateOverrideSpan's
  // Freeze arm), reading any existing Override's Windows first — nothing is ever replaced, so
  // there is nothing to confirm and no clobber-check round trip to make.
  if (span.mode === 'freeze') return sendJson<CreatedDays>('POST', '/api/overrides', span)
  const dates = await getJson<AffectedDates>(`/api/overrides/clobber-check?from=${span.from}&to=${span.to}`)
  if (dates === null) throw new Error('Clobber check returned no dates response')
  if (dates.length && !await confirm(dates, spanSize(span.from, span.to), span.mode)) return null
  return sendJson<CreatedDays>('POST', '/api/overrides', span)
}

function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false
  const date = new Date(`${value}T00:00:00Z`)
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
}

// Inclusive day count across a from..to span already validated as calendar dates at UTC midnight.
function spanSize(from: string, to: string): number {
  return Math.round((Date.parse(to) - Date.parse(from)) / 86400000) + 1
}
