import { useEffect, useRef, useState } from 'react'
import { OverrideSheet } from './OverrideSheet'

export function useOverrideConfirmation() {
  const [dates, setDates] = useState<readonly string[] | null>(null)
  const answer = useRef<((accepted: boolean) => void) | null>(null)
  useEffect(() => () => { answer.current?.(false) }, [])
  function finish(accepted: boolean) {
    answer.current?.(accepted)
    answer.current = null
    setDates(null)
  }
  function confirm(affected: readonly string[]): Promise<boolean> {
    setDates(affected)
    return new Promise(resolve => { answer.current = resolve })
  }
  const presentation = dates && <OverrideSheet title="Replace Overrides?" onCancel={() => finish(false)}>
    <div className="note">These dates already have an Override. Replace their windows?</div>
    <div className="list">{dates.map(date => <div className="row" key={date}><div className="body"><div className="title">{date}</div></div></div>)}</div>
    <div className="btn-row"><button className="btn danger wide" onClick={() => finish(true)}>Replace Overrides</button></div>
  </OverrideSheet>
  return { confirm, presentation }
}
