import { useEffect, useRef, useState } from 'react'
import { OverrideSheet } from './OverrideSheet'
import { fmtShort } from './OverrideFormat'

interface Clobber { readonly dates: readonly string[]; readonly span: number }

export function useOverrideConfirmation() {
  const [clobber, setClobber] = useState<Clobber | null>(null)
  const answer = useRef<((accepted: boolean) => void) | null>(null)
  useEffect(() => () => { answer.current?.(false) }, [])
  function finish(accepted: boolean) {
    answer.current?.(accepted)
    answer.current = null
    setClobber(null)
  }
  function confirm(affected: readonly string[], span: number): Promise<boolean> {
    setClobber({ dates: affected, span })
    return new Promise(resolve => { answer.current = resolve })
  }
  const presentation = clobber && (() => {
    const { dates, span } = clobber
    const n = dates.length
    const untouched = span - n
    // Dates render through fmtShort, never ISO. No per-date shape detail: clobber-check returns
    // bare dates and DayShape carries no template name, so the .pill.due is what is actually true.
    return <OverrideSheet title={`Replace ${n} Override${n === 1 ? '' : 's'}?`} onCancel={() => finish(false)}>
      <div className="damage">
        <div className="damage-h">{n} of the {span} dates in this span already depart from the pattern.
          Stamping replaces what is on them.</div>
        {dates.map(date => <div className="row" key={date}><div className="body">
          <div className="title">{fmtShort(date)}</div>
          <div className="meta"><span className="pill due">already an override</span></div>
        </div></div>)}
      </div>
      <div className="note">
        {untouched > 0 && <>The other {untouched} dates are following the pattern and will be copied off it. </>}
        Nothing here can be undone in one step — reverting is per date.
      </div>
      <div className="btn-row"><button className="btn danger wide" onClick={() => finish(true)}>
        {untouched > 0 ? `Replace ${n} and stamp all ${span}` : `Replace all ${n}`}
      </button></div>
    </OverrideSheet>
  })()
  return { confirm, presentation }
}
