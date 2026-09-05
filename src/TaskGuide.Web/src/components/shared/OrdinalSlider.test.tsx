import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { OrdinalSlider } from './OrdinalSlider'

const VALUES = ['whisper', 'quiet', 'normal', 'loud'] as const

describe('OrdinalSlider', () => {
  it('renders a labelled tick for every value, in order', () => {
    const { container } = render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" onChange={() => {}} />)

    const ticks = container.querySelectorAll('.ticks span')
    expect(Array.from(ticks).map((t) => t.textContent)).toEqual(['whisper', 'quiet', 'normal', 'loud'])
  })

  it('shows a hint naming the set value', () => {
    const { container } = render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" onChange={() => {}} />)

    expect(container.querySelector('.hint')?.textContent).toBe('Set to quiet.')
  })

  it('calls onChange with the value at the new slider index', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value="whisper" onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Volume'), { target: { value: '2' } })

    expect(onChange).toHaveBeenCalledWith('normal')
  })

  it('shows a "leave at the default" toggle when a default is declared, pressed when unset', () => {
    const { rerender } = render(
      <OrdinalSlider label="Volume" values={VALUES} value="loud" defaultValue="normal" onChange={() => {}} />,
    )
    expect(screen.getByRole('button', { name: /leave at the default \(normal\)/i })).toHaveAttribute(
      'aria-pressed',
      'false',
    )

    rerender(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} />)
    expect(screen.getByRole('button', { name: /leave at the default \(normal\)/i })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('choosing "leave at the default" clears the value to null', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value="loud" defaultValue="normal" onChange={onChange} />)

    fireEvent.click(screen.getByRole('button', { name: /leave at the default/i }))

    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('has no "leave at the default" control when no default is declared', () => {
    render(<OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} />)

    expect(screen.queryByRole('button', { name: /leave at the default/i })).not.toBeInTheDocument()
  })

  it('dims the slider (a class, not inline style) and shows index 0 while unset', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} />)

    const el = screen.getByLabelText('Volume')
    expect(el).toHaveClass('unset')
    expect(el).not.toHaveAttribute('style')
    expect(el).toHaveValue('0')
  })

  it('the hint explains an unset value carries the default', () => {
    const { container } = render(
      <OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} />,
    )

    expect(container.querySelector('.hint')?.textContent).toBe(
      'Nothing chosen — the slider is showing whisper but the task carries the default. Touch it to commit a value.',
    )
  })

  it('touching the slider while unset commits a value', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Volume'), { target: { value: '2' } })

    expect(onChange).toHaveBeenCalledWith('normal')
  })

  it('renders read-only with the same control structure, input disabled', () => {
    const { container } = render(
      <OrdinalSlider label="Volume" values={VALUES} value="quiet" defaultValue="normal" onChange={() => {}} readOnly />,
    )

    expect(screen.getByLabelText('Volume')).toBeDisabled()
    expect(screen.getByRole('button', { name: /leave at the default/i })).toBeDisabled()
    expect(container.querySelectorAll('.ticks span')).toHaveLength(4)
    expect(container.querySelector('.hint')).not.toBeNull()
  })

  // ADR-0006: the range input must survive its own change event, and must not remount when a
  // sibling control (the default toggle) changes state either.
  it('the range input survives its own input event — same DOM node before and after', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="whisper" onChange={() => {}} />)

    const el = screen.getByLabelText('Volume')
    fireEvent.change(el, { target: { value: '3' } })

    expect(screen.getByLabelText('Volume')).toBe(el)
  })

  it('the range input survives a press of the default toggle', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="loud" defaultValue="normal" onChange={() => {}} />)

    const el = screen.getByLabelText('Volume')
    fireEvent.click(screen.getByRole('button', { name: /leave at the default/i }))

    expect(screen.getByLabelText('Volume')).toBe(el)
  })
})
