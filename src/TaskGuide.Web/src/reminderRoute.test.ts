import { describe, expect, it } from 'vitest'
import { parseReminderRoute } from './reminderRoute'

describe('parseReminderRoute', () => {
  it('parses a window route and preserves the window identity', () => {
    expect(parseReminderRoute('/2026-09-15/w_evening')).toEqual({ date: '2026-09-15', windowId: 'w_evening' })
  })

  it('parses the fallback route as the literal fallback identity', () => {
    expect(parseReminderRoute('/2026-09-15/fallback')).toEqual({ date: '2026-09-15', windowId: 'fallback' })
  })

  it('preserves an unknown window identity instead of mapping it to fallback', () => {
    expect(parseReminderRoute('/2026-09-15/w_not_in_today_shape')).toEqual({
      date: '2026-09-15',
      windowId: 'w_not_in_today_shape',
    })
  })

  it.each(['/2026-09-15', '/2026-02-30/w_evening', '/0000-01-01/w_evening', '/not-a-date/w_evening', '/2026-09-15/', '/2026-09-15/a/b'])('rejects malformed route shape %s', (path) => {
    expect(parseReminderRoute(path)).toBeNull()
  })
})
