import { useState } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { OrdinalSlider } from './OrdinalSlider'

const VALUES = ['whisper', 'quiet', 'normal', 'loud'] as const

// A controlled parent that actually applies onChange, standing in for a real caller — needed
// whenever a test must observe `unset` really flipping (the component itself never reads its own
// prop back).
function Controlled({ initial = null as string | null }) {
  const [value, setValue] = useState<string | null>(initial)
  return <OrdinalSlider label="Volume" values={VALUES} value={value} defaultValue="normal" onChange={setValue} />
}

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

  // #147: the keyboard commit path is gone, so an unset slider needs a button — a range parked at
  // index 0 fires no `change` for a decrementing key or a drag-to-0, and there is otherwise no way
  // to commit the least value.
  it('offers a "Use <least value>" button in the chipset while unset, and clicking it commits values[0]', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const button = screen.getByRole('button', { name: 'Use whisper' })
    expect(button.closest('.chipset')).not.toBeNull()

    fireEvent.click(button)

    expect(onChange).toHaveBeenCalledWith('whisper')
  })

  // Duration declares no default, so the chipset used to be entirely absent for it. With the
  // keyboard path deleted, that would leave a Duration slider parked at index 0 with no way at all
  // to commit — the wrapper's condition must widen to `hasDefault || unset`.
  it('shows the chipset with the "Use <least value>" button but no "Leave at the default" button when no default is declared', () => {
    render(<OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} />)

    expect(screen.getByRole('button', { name: 'Use whisper' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /leave at the default/i })).not.toBeInTheDocument()
  })

  it('hides the "Use <least value>" button once a value is set, while "Leave at the default" remains', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" defaultValue="normal" onChange={() => {}} />)

    expect(screen.queryByRole('button', { name: 'Use whisper' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /leave at the default/i })).toBeInTheDocument()
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
      'Nothing chosen — the slider is showing whisper but the task carries the default. Drag it, or press "Use whisper", to commit a value.',
    )
  })

  // Review finding 5: the no-default case is the one where the button is the *only* way to
  // commit values[0] (no "Leave at the default" fallback exists), so its hint must name it too.
  it('the no-default hint names the "Use <least value>" button', () => {
    const { container } = render(<OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} />)

    expect(container.querySelector('.hint')?.textContent).toBe(
      'Not set. Drag the slider, or press "Use whisper", to commit a value.',
    )
  })

  it('touching the slider while unset commits a value', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Volume'), { target: { value: '2' } })

    expect(onChange).toHaveBeenCalledWith('normal')
  })

  // Review finding 2: while unset the thumb already sits at index 0, so dragging it TO index 0
  // fires no `change` event at all — a user can never explicitly commit the least value that way.
  // A pointerUp (or click) on the slider while unset must commit the value it's currently showing.
  it('committing index 0 while unset (no change event fires) still commits the least value', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.pointerDown(slider)
    fireEvent.pointerUp(slider)

    expect(onChange).toHaveBeenCalledWith('whisper')
  })

  it('does not commit a pointer release that did not start on the slider', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    fireEvent.pointerUp(screen.getByLabelText('Volume'))

    expect(onChange).not.toHaveBeenCalled()
  })

  it('committing on pointerUp while unset does not remount the slider', () => {
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} />)

    const el = screen.getByLabelText('Volume')
    fireEvent.pointerDown(el)
    fireEvent.pointerUp(el)

    expect(screen.getByLabelText('Volume')).toBe(el)
  })

  it('does not re-commit on pointerUp once a value is already set', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.pointerDown(slider)
    fireEvent.pointerUp(slider)

    expect(onChange).not.toHaveBeenCalled()
  })

  // A parent that defers applying onChange (an awaited write, startTransition) leaves `value`
  // still null after `change` fires — so pointerUp's `unset` check is still true and would fire a
  // second, wrong onChange(values[0]), downgrading the click. Standing in for that parent: this
  // one ignores onChange entirely, so `value` never changes.
  it('does not downgrade a click to the least value when the parent has not applied the change yet', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.pointerDown(slider)
    fireEvent.change(slider, { target: { value: '2' } })
    fireEvent.pointerUp(slider)

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('normal')
  })

  // Review finding 6: a value that isn't in `values` gives indexOf === -1. React would then write
  // value="-1" on the input, the browser clamps the visible thumb to values[0], but `unset` wasn't
  // set — the control showed one value (whisper, via the clamp) while the hint claimed "Set to
  // <the missing value>." A value not found in the set must fall back to the unset presentation.
  it('falls back to the unset presentation when the value is not in the set', () => {
    const { container } = render(
      <OrdinalSlider label="Volume" values={VALUES} value="deafening" defaultValue="normal" onChange={() => {}} />,
    )

    const el = screen.getByLabelText('Volume')
    expect(el).toHaveClass('unset')
    expect(el).toHaveValue('0')
    expect(container.querySelector('.hint')?.textContent).not.toContain('deafening')
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

  // Review finding 1: a second pointerdown mid-gesture (a stray pointer, a second finger) must not
  // reset `changedDuringGesture` — otherwise, with a deferring parent, a click at index 2 fires
  // `change` -> onChange('normal'), the second pointerdown clears the guard, and pointerUp fires a
  // downgrading onChange('whisper') on top of it.
  it('a second pointerdown mid-gesture does not defeat the downgrade guard', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.pointerDown(slider)
    fireEvent.change(slider, { target: { value: '2' } })
    fireEvent.pointerDown(slider)
    fireEvent.pointerUp(slider)

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('normal')
  })

  // Review finding 2: the "Use <least value>" button is the sole replacement for the deleted
  // keyboard commit path, so it must not drop focus when it unmounts on activation.
  it('moves focus to the slider when the "Use <least value>" button commits and unmounts', () => {
    render(<Controlled />)

    const button = screen.getByRole('button', { name: 'Use whisper' })
    button.focus()
    fireEvent.click(button)

    expect(document.activeElement).toBe(screen.getByLabelText('Volume'))
  })

  // Review finding 7: DimensionsScreen's read-only catalog always passes value={null}, so `unset`
  // is permanently true there — a disabled action that can never fire is dead UI on a read-only
  // viewer.
  it('does not render the "Use <least value>" button when read-only', () => {
    render(
      <OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} readOnly />,
    )

    expect(screen.queryByRole('button', { name: 'Use whisper' })).not.toBeInTheDocument()
  })

  // Review finding 1 (7th pass): a read-only viewer can't drag a disabled slider or press a
  // button that isn't rendered, so the hint must not tell them to.
  it('the read-only unset hint (no default) omits the instruction to drag or press a button', () => {
    const { container } = render(
      <OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} readOnly />,
    )

    expect(container.querySelector('.hint')?.textContent).toBe('Not set.')
    expect(container.querySelector('.chipset')).toBeNull()
  })

  it('the read-only unset hint (with default) omits the instruction to drag or press a button', () => {
    const { container } = render(
      <OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={() => {}} readOnly />,
    )

    expect(container.querySelector('.hint')?.textContent).toBe(
      'Nothing chosen — the slider is showing whisper but the task carries the default.',
    )
    expect(screen.getByRole('button', { name: /leave at the default \(normal\)/i })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /use whisper/i })).not.toBeInTheDocument()
  })

  // Review finding 2 (7th pass): with no declared default, the chipset wrapper rendered an empty
  // `.chipset` div on a read-only unset slider (both its children independently gated off) — a
  // stray 8px flex-gap contributor with nothing in it.
  it('renders no chipset at all when read-only, unset, and no default is declared', () => {
    const { container } = render(<OrdinalSlider label="Duration" values={VALUES} value={null} onChange={() => {}} readOnly />)

    expect(container.querySelector('.chipset')).toBeNull()
  })

})
