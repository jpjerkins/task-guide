import { useEffect, useRef, useState } from 'react'
import { OverrideSheet } from './OverrideSheet'
import { fmtShort } from './OverrideFormat'

// Freeze never reaches this confirmation (authorOverrideSpan skips the clobber-check entirely for
// it — nothing is ever replaced), so this hook only ever needs to distinguish stamp from blank.
type Mode = 'stamp' | 'blank'
interface Clobber { readonly dates: readonly string[]; readonly span: number; readonly mode: Mode }

export function useOverrideConfirmation() {
  const [clobber, setClobber] = useState<Clobber | null>(null)
  const answer = useRef<((accepted: boolean) => void) | null>(null)
  useEffect(() => () => { answer.current?.(false) }, [])
  function finish(accepted: boolean) {
    answer.current?.(accepted)
    answer.current = null
    setClobber(null)
  }
  function confirm(affected: readonly string[], span: number, mode: Mode): Promise<boolean> {
    setClobber({ dates: affected, span, mode })
    return new Promise(resolve => { answer.current = resolve })
  }
  const presentation = clobber && (() => {
    const { dates, span, mode } = clobber
    const n = dates.length
    const untouched = span - n
    // A single date stamped on its own is the commonest path, and it is not a span: "Replace all 1"
    // and "1 of the 1 dates in this span" both read as machine output.
    const alone = span === 1 && n === 1
    const blank = mode === 'blank'
    const verb = blank ? 'Blank' : 'Replace'
    // Blanking clears the clobbered dates rather than stamping over them, so the same structure
    // needs different verbs — never "stamp" or "copied off" outside the stamp arm.
    const action = blank ? 'Blanking clears' : 'Stamping replaces'
    // n === 0 is only reachable in blank mode (#152: blank confirms on span size, not clobber
    // count — stamp still skips this hook entirely when nothing is clobbered). No date departs
    // from the pattern yet, so there is nothing true to list — no .damage block at all.
    if (n === 0) {
      const soloSpan = span === 1
      return <OverrideSheet title={soloSpan ? 'Blank this date?' : `Blank all ${span} dates?`} onCancel={() => finish(false)}>
        <div className="damage-h">{soloSpan
          ? 'Every window on this date is removed and nothing will fire on it.'
          : 'Every window on these dates is removed and nothing will fire on them.'}</div>
        <div className="note">Nothing here can be undone in one step — reverting is per date.</div>
        <div className="btn-row"><button className="btn danger wide" onClick={() => finish(true)}>
          {soloSpan ? 'Blank it' : `Blank all ${span}`}
        </button></div>
      </OverrideSheet>
    }
    // Dates render through fmtShort, never ISO. No per-date shape detail: clobber-check returns
    // bare dates and DayShape carries no template name, so the .pill.due is what is actually true.
    return <OverrideSheet title={`${verb} ${n} Override${n === 1 ? '' : 's'}?`} onCancel={() => finish(false)}>
      <div className="damage">
        <div className="damage-h">{alone
          ? `This date already departs from the pattern. ${action} what is on it.`
          : `${n} of the ${span} dates in this span already depart from the pattern. ${action} what is on them.`}</div>
        {dates.map(date => <div className="row" key={date}><div className="body">
          <div className="title">{fmtShort(date)}</div>
          <div className="meta"><span className="pill due">already an override</span></div>
        </div></div>)}
      </div>
      <div className="note">
        {untouched > 0 && <>The other {untouched} date{untouched === 1 ? '' : 's'} {untouched === 1 ? 'is' : 'are'} following the pattern and will be {blank ? 'cleared too' : 'copied off it'}. </>}
        Nothing here can be undone in one step — reverting is per date.
      </div>
      <div className="btn-row"><button className="btn danger wide" onClick={() => finish(true)}>
        {blank
          ? (alone ? 'Blank it' : `Blank all ${span}`)
          : (alone ? 'Replace it' : untouched > 0 ? `Replace ${n} and stamp all ${span}` : `Replace all ${n}`)}
      </button></div>
    </OverrideSheet>
  })()
  return { confirm, presentation }
}
