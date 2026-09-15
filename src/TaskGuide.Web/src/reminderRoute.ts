export type ReminderRoute = {
  date: string
  windowId: string
}

export function parseReminderRoute(pathname: string): ReminderRoute | null {
  const segments = pathname.split('/')
  if (segments.length !== 3 || segments[0] !== '' || segments[1] === '' || segments[2] === '') return null

  const date = segments[1]
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date.startsWith('0000')) return null
  const parsedDate = new Date(`${date}T00:00:00Z`)
  if (Number.isNaN(parsedDate.getTime()) || parsedDate.toISOString().slice(0, 10) !== date) return null

  let windowId: string
  try {
    windowId = decodeURIComponent(segments[2])
  } catch {
    return null
  }
  if (windowId === '' || windowId.includes('/')) return null

  return { date, windowId }
}
