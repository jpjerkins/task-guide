import { describe, expect, it } from 'vitest'
import { fmtShort, hm } from './OverrideFormat'

describe('hm', () => {
  it('renders a time the way the prototype says it out loud', () => {
    expect(hm('06:00')).toBe('6a')
    expect(hm('07:30')).toBe('7:30a')
    expect(hm('12:00')).toBe('12p')
    expect(hm('00:00')).toBe('12a')
    expect(hm('15:00')).toBe('3p')
    expect(hm('23:45')).toBe('11:45p')
  })

  it('tolerates a wire time carrying seconds', () => {
    expect(hm('15:00:00')).toBe('3p')
  })
})

describe('fmtShort', () => {
  it('renders an ISO date as the prototype does, in Chicago', () => {
    expect(fmtShort('2026-09-15')).toBe('Tue 15 Sep')
    expect(fmtShort('2026-01-01')).toBe('Thu 1 Jan')
  })
})
