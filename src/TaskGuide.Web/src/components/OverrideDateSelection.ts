import { useMemo, useState } from 'react'
import type { ComponentProps } from 'react'
import type { DateEntry } from './shared/DateEntry'

// Keep date-control state independent of the fixed rail span; render through the shared DateEntry.
export function useOverrideDateSelection(now: Date = new Date()) {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Chicago' }).format(now)
  const [selectedDate, setSelectedDate] = useState(today)
  const [pickedDate, setPickedDate] = useState<string | null>(today)
  const railSpan = useMemo(() => ({ from: offsetDate(today, -10), to: offsetDate(today, 10) }), [today])
  const dateEntryProps: ComponentProps<typeof DateEntry> = {
    label: 'Pick a date…', value: pickedDate,
    onChange: (date) => {
      setPickedDate(date)
      if (date !== null) setSelectedDate(date)
    },
  }
  return { selectedDate, railSpan, dateEntryProps }
}

function offsetDate(date: string, days: number): string {
  // Calendar arithmetic on an already-resolved Chicago date; elapsed hours would drift over DST.
  const shifted = new Date(`${date}T00:00:00Z`)
  shifted.setUTCDate(shifted.getUTCDate() + days)
  return shifted.toISOString().slice(0, 10)
}
