import { useEffect, useRef } from 'react'

interface DateRailProps {
  from: string
  to: string
  selected: string
  marked: readonly string[]
  onSelect: (date: string) => void
  disabled?: boolean
}
// schedule-editing prototype W.date(); the inventory corrects its 11 dates to ±10.
export function DateRail({ from, to, selected, marked, onSelect, disabled }: DateRailProps) {
  const dates: string[] = []
  const cursor = new Date(`${from}T00:00:00Z`)
  while (cursor.toISOString().slice(0, 10) <= to) {
    dates.push(cursor.toISOString().slice(0, 10))
    cursor.setUTCDate(cursor.getUTCDate() + 1)
  }
  const selectedRef = useRef<HTMLButtonElement>(null)
  // #140: the rail opens showing last week with today off the right edge because nothing ever
  // scrolls it into view. This scrolls an existing node on mount/selection change — it neither
  // remounts nor re-renders anything, so ADR-0006 (controls surviving their own input events)
  // does not apply. `block: 'nearest'` keeps the browser from also scrolling the page to the rail.
  useEffect(() => { selectedRef.current?.scrollIntoView({ block: 'nearest', inline: 'center' }) }, [selected])
  return <div className="weekstrip">{dates.map(date => <button key={date}
    ref={date === selected ? selectedRef : undefined}
    aria-label={date} aria-current={date === selected ? 'date' : undefined}
    disabled={disabled} onClick={() => onSelect(date)}>
    <div className="wd">{new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'UTC' }).format(new Date(`${date}T00:00:00Z`))}</div>
    <div className="dn">{Number(date.slice(8))}</div>
    <div className={`dot${marked.includes(date) ? '' : ' blank'}`} />
  </button>)}</div>
}
