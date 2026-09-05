import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { OrdinalSlider } from './OrdinalSlider'

const VALUES = ['whisper', 'quiet', 'normal', 'loud'] as const

describe('OrdinalSlider', () => {
  it('renders the current value label', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" onChange={() => {}} />)

    expect(screen.getByText('quiet')).toBeInTheDocument()
  })

  it('calls onChange with the value at the new slider index', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value="whisper" onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Volume'), { target: { value: '2' } })

    expect(onChange).toHaveBeenCalledWith('normal')
  })

  it('shows a "leave at the default" control when a default is declared, and choosing it clears the value', () => {
    const onChange = vi.fn()
    render(
      <OrdinalSlider label="Volume" values={VALUES} value="loud" defaultValue="normal" onChange={onChange} />,
    )

    fireEvent.click(screen.getByLabelText(/leave at the default/i))

    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('has no "leave at the default" control when no default is declared', () => {
    render(<OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} />)

    expect(screen.queryByLabelText(/leave at the default/i)).not.toBeInTheDocument()
  })

  it('renders read-only with the same control structure, input disabled', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" onChange={() => {}} readOnly />)

    expect(screen.getByLabelText('Volume')).toBeDisabled()
  })

  // ADR-0006: the range input must survive its own change event.
  it('survives its own input event — same DOM node before and after', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="whisper" onChange={() => {}} />)

    const el = screen.getByLabelText('Volume')
    fireEvent.change(el, { target: { value: '3' } })

    expect(screen.getByLabelText('Volume')).toBe(el)
  })
})
