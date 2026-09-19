// The two display formats every authoring surface in #140 shares. It lives under the `Override*`
// glob because that is what the ticket's Owns block covers; the Event sheet imports it from here
// rather than holding a third copy (ReminderPage.tsx already holds a cross-lane one, deliberately).

// "3p", "7:30a", "12p". Storage and the wire stay 24h. Ported verbatim from ui-screens.prototype.html's
// hm() (~568). No client clock is involved — this only formats server-given strings.
export function hm(time: string): string {
  const h = Number(time.slice(0, 2))
  const m = Number(time.slice(3, 5))
  const h12 = h % 12 === 0 ? 12 : h % 12
  return (m ? `${h12}:${String(m).padStart(2, '0')}` : `${h12}`) + (h < 12 ? 'a' : 'p')
}

// "Tue 15 Sep" — schedule-editing.prototype.html's fmtShort() (~359). The ISO date is a Chicago
// calendar date already (DayBoundary.ZoneId), so it is formatted as UTC to keep the browser's own
// zone from shifting the day.
export function fmtShort(date: string): string {
  const parts = new Intl.DateTimeFormat('en-US', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' })
    .formatToParts(new Date(`${date}T00:00:00Z`))
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? ''
  return `${part('weekday')} ${part('day')} ${part('month')}`
}
