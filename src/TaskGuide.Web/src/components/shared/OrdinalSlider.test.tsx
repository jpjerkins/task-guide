import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
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

  it('commits the value it is showing from any keystroke while unset, not just a chosen key list (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowLeft' })
    fireEvent.keyUp(slider, { key: 'ArrowLeft' })

    expect(onChange).toHaveBeenCalledWith('whisper')
  })

  // #147: ArrowDown is a decrementing key too, but the range input parked at index 0 fires no
  // `change` for it (same as ArrowLeft/Home) — the old key-list guard missed it. The fix is
  // deliberately keyless, so any key the guard used to miss now commits too.
  it('commits from unset on a key the old ArrowLeft/Home-only guard missed (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowDown' })
    fireEvent.keyUp(slider, { key: 'ArrowDown' })

    expect(onChange).toHaveBeenCalledWith('whisper')
  })

  // #147 (4th review pass): Cmd/Ctrl/Alt+arrow doesn't move a range input, but keydown and keyup
  // both still land on the focused slider and the key name is in RANGE_KEYS — so the old guard
  // committed values[0] on a control the user never meant to touch (Cmd+ArrowDown scrolls the
  // page on macOS). Shift+arrow is different: a range input DOES move for it, so Shift must not
  // be excluded the same way.
  it('does not commit on a modified arrow (Cmd/Ctrl/Alt), but Shift+arrow still commits (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowDown', metaKey: true })
    fireEvent.keyUp(slider, { key: 'ArrowDown', metaKey: true })

    expect(onChange).not.toHaveBeenCalled()

    fireEvent.keyDown(slider, { key: 'ArrowDown', shiftKey: true })
    fireEvent.keyUp(slider, { key: 'ArrowDown', shiftKey: true })

    expect(onChange).toHaveBeenCalledWith('whisper')
  })

  // #147 (2nd review pass): the keyup handler's job is "commit the value being shown if this
  // keystroke moved nothing" — once a `change` fires during the keypress, the keystroke already
  // committed, whatever a deferring parent (an awaited API call, `startTransition`) has applied
  // yet. A parent that ignores onChange stands in for that lag: `value` stays null, so `unset` is
  // still true at keyup, but the guard must not fire a second, stale commit.
  it('does not also commit on keyup once a change fired during the same keystroke, even if the parent has not applied it (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowRight' })
    fireEvent.change(slider, { target: { value: '1' } })
    fireEvent.keyUp(slider, { key: 'ArrowRight' })

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('quiet')
  })

  // #147: keyup is dispatched to the element focused AT keyup time, not keydown time. Tabbing
  // INTO a slider fires keydown on the element being left, then keyup lands on the slider — so
  // without provenance tracking, tabbing over an unset slider would commit values[0] on a control
  // the user never touched.
  it('does not commit on Tab landing on an unset slider (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    fireEvent.keyUp(screen.getByLabelText('Volume'), { key: 'Tab' })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('does not re-commit on keyUp once a value is already set (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value="quiet" defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowDown' })
    fireEvent.keyUp(slider, { key: 'ArrowDown' })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('committing on keyUp while unset does not remount the slider (#147)', () => {
    // A parent that ignores onChange can't tell a remount from a no-op re-render, so this wraps
    // a stateful parent that actually applies the commit — proving the input survives a real
    // value-prop change, not just an event that nothing downstream reacted to.
    function StatefulSlider() {
      const [value, setValue] = useState<string | null>(null)
      return <OrdinalSlider label="Volume" values={VALUES} value={value} defaultValue="normal" onChange={setValue} />
    }
    render(<StatefulSlider />)

    const el = screen.getByLabelText('Volume')
    expect(screen.getByText(/Nothing chosen/)).toBeInTheDocument()

    fireEvent.keyDown(el, { key: 'ArrowDown' })
    fireEvent.keyUp(el, { key: 'ArrowDown' })

    expect(screen.getByLabelText('Volume')).toBe(el)
    expect(screen.getByText('Set to whisper.')).toBeInTheDocument()
  })

  // #147 (3rd review pass): keyStartedOnSlider is only ever cleared on THIS control's keyup, but
  // Tab moves focus on keydown — tabbing forward out of an unset slider fires keydown here
  // (setting the ref true) then the keyup lands on the NEXT control, so this slider's keyup never
  // runs and the ref stays stale. Shift+Tab back in then sees a stale `true` with
  // changedDuringKeypress still false and wrongly commits values[0]. Clearing the ref on blur
  // mirrors onPointerCancel's guard on the pointer path.
  it("does not commit a range-key keyup whose keydown landed elsewhere, after a Tab-out consumed this control's keyup (stale keyStartedOnSlider) (#147)", () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    // Tab forward out: keydown lands here, focus moves before keyup fires, so this control's
    // keyup never runs and only the blur clears the flag.
    fireEvent.keyDown(slider, { key: 'Tab' })
    fireEvent.blur(slider)
    // A later range-key keyup arrives with no keydown having landed here — focus returned mid
    // keystroke (a held arrow released after focus moved back). Deliberately a RANGE_KEYS key:
    // asserting with Tab/Shift would pass on the key list alone and never exercise the blur.
    fireEvent.keyUp(slider, { key: 'ArrowDown' })

    expect(onChange).not.toHaveBeenCalled()
  })

  // #147 (3rd review pass): a held key auto-repeats, firing repeated keydowns during one
  // keystroke. changedDuringKeypress must survive those repeats — resetting it on every keydown
  // would erase the record of a `change` that already fired earlier in the same keystroke, and a
  // parent that defers applying onChange (an awaited API write, `startTransition`) would then see
  // the keyup wrongly re-commit values[0].
  it('does not erase a same-keystroke change on an auto-repeated keydown (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowRight' })
    fireEvent.change(slider, { target: { value: '1' } })
    fireEvent.keyDown(slider, { key: 'ArrowRight', repeat: true })
    fireEvent.keyUp(slider, { key: 'ArrowRight' })

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('quiet')
  })

  // #147 (4th review pass): `!e.repeat` only guards a HELD key's auto-repeat, not a different key
  // pressed before the first is released — that keydown is not a repeat, so it still reset the
  // flag. With a deferring parent (value stays null until the write lands): ArrowRight fires
  // `change` and sets changedDuringKeypress, then ArrowLeft's keydown (before ArrowRight's keyup)
  // wrongly cleared it, so ArrowLeft's own keyup re-committed values[0] over the value just chosen.
  it('does not erase a same-keystroke change when a second key is pressed before the first is released (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'ArrowRight' })
    fireEvent.change(slider, { target: { value: '1' } })
    fireEvent.keyDown(slider, { key: 'ArrowLeft' })
    fireEvent.keyUp(slider, { key: 'ArrowLeft' })

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('quiet')
  })

  // Phil's call, 2026-09-21: provenance alone stops traversal commits, but a key aimed at the
  // form — Enter to submit, Escape to dismiss, Ctrl+S — still starts and ends on a focused,
  // untouched slider. Require the key be one a range input actually responds to.
  it('does not commit on Enter (key not aimed at the slider) (#147)', () => {
    const onChange = vi.fn()
    render(<OrdinalSlider label="Volume" values={VALUES} value={null} defaultValue="normal" onChange={onChange} />)

    const slider = screen.getByLabelText('Volume')
    fireEvent.keyDown(slider, { key: 'Enter' })
    fireEvent.keyUp(slider, { key: 'Enter' })

    expect(onChange).not.toHaveBeenCalled()
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
})
