import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DateEntry } from './DateEntry'

describe('DateEntry', () => {
  it('renders the given ISO value in a date input', () => {
    render(<DateEntry label="Deadline" value="2026-09-04" onChange={() => {}} />)

    expect(screen.getByLabelText('Deadline')).toHaveValue('2026-09-04')
  })

  it('renders a null value as blank', () => {
    render(<DateEntry label="Deadline" value={null} onChange={() => {}} />)

    expect(screen.getByLabelText('Deadline')).toHaveValue('')
  })

  it('calls onChange with the new ISO value', () => {
    const onChange = vi.fn()
    render(<DateEntry label="Deadline" value={null} onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Deadline'), { target: { value: '2026-09-05' } })

    expect(onChange).toHaveBeenCalledWith('2026-09-05')
  })

  it('calls onChange with null when cleared', () => {
    const onChange = vi.fn()
    render(<DateEntry label="Deadline" value="2026-09-04" onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Deadline'), { target: { value: '' } })

    expect(onChange).toHaveBeenCalledWith(null)
  })

  // ADR-0006: a system-presented control must survive its own input events. The remount is what
  // dismisses a native picker mid-interaction on iOS Safari — so the input node identity must be
  // stable across its own change handler firing.
  it('survives its own input event — same DOM node before and after', () => {
    render(<DateEntry label="Deadline" value={null} onChange={() => {}} />)

    const el = screen.getByLabelText('Deadline')
    fireEvent.change(el, { target: { value: '2026-09-05' } })

    expect(screen.getByLabelText('Deadline')).toBe(el)
  })
})
