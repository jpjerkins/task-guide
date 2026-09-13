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
  return <div className="weekstrip">{dates.map(date => <button key={date}
    aria-label={date} aria-current={date === selected ? 'date' : undefined}
    disabled={disabled} onClick={() => onSelect(date)}>
    <div className="wd">{new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'UTC' }).format(new Date(`${date}T00:00:00Z`))}</div>
    <div className="dn">{Number(date.slice(8))}</div>
    <div className={`dot${marked.includes(date) ? '' : ' blank'}`} />
  </button>)}</div>
}
