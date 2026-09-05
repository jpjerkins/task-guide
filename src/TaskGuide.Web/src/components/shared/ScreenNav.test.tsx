import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { registerQuickAction, resetRegistry } from './screenRegistry'
import { BackProvider, ScreenNav } from './ScreenNav'

beforeEach(() => {
  resetRegistry()
})

describe('ScreenNav', () => {
  it('renders the title', () => {
    render(<ScreenNav title="Schedule" />)

    expect(screen.getByRole('heading', { name: 'Schedule' })).toBeInTheDocument()
  })

  it('renders no back control when back is not given', () => {
    render(<ScreenNav title="Schedule" />)

    expect(screen.queryByText(/back/i)).not.toBeInTheDocument()
  })

  it('renders the back control and calls onBack when given', () => {
    const onBack = vi.fn()
    render(<ScreenNav title="Sched A" back={{ onBack }} />)

    fireEvent.click(screen.getByText(/back/i))

    expect(onBack).toHaveBeenCalled()
  })

  it('renders the back control from a BackProvider ancestor when no explicit back prop is given', () => {
    const onBack = vi.fn()
    render(
      <BackProvider value={{ onBack }}>
        <ScreenNav title="Sched A" />
      </BackProvider>,
    )

    fireEvent.click(screen.getByText(/back/i))

    expect(onBack).toHaveBeenCalled()
  })

  it('an explicit back prop wins over a BackProvider ancestor', () => {
    const contextOnBack = vi.fn()
    const propOnBack = vi.fn()
    render(
      <BackProvider value={{ onBack: contextOnBack }}>
        <ScreenNav title="Sched A" back={{ onBack: propOnBack }} />
      </BackProvider>,
    )

    fireEvent.click(screen.getByText(/back/i))

    expect(propOnBack).toHaveBeenCalled()
    expect(contextOnBack).not.toHaveBeenCalled()
  })

  it('renders nothing in the quick-action slot when nothing is registered', () => {
    render(<ScreenNav title="Schedule" />)

    expect(screen.queryByLabelText('Quick add')).not.toBeInTheDocument()
  })

  it('renders the registered quick action in the right slot', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)

    render(<ScreenNav title="Schedule" />)

    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })

  // Review finding 7: `{renderQuickAction && renderQuickAction()}` called the registrant's
  // renderer as a plain function during ScreenNav's own render — not as a JSX element, so it gets
  // no component boundary of its own. A renderer that keeps state directly in its body (exactly
  // like #103's quick action will need — an open-sheet flag, the same shape as TasksScreen's
  // sheetOpen) then adds a hook to *ScreenNav's* hook list, conditionally, only on renders where
  // `renderQuickAction` is truthy. If registration happens after ScreenNav's first render (this
  // test's beforeEach resets the registry, so the very first render always sees nothing
  // registered), the second render adds a hook ScreenNav's first render never had — React's Rules
  // of Hooks violation ("more hooks than during the previous render"). Rendering the slot as its
  // own always-mounted component sidesteps this: the renderer's hooks belong to that component's
  // own, separate, consistently-present instance instead.
  it('does not violate Rules of Hooks when a quick action with its own hook registers after ScreenNav has already rendered', () => {
    const { rerender } = render(<ScreenNav title="Schedule" />)

    registerQuickAction(() => {
      const [count] = useState(0)
      return <button aria-label="Quick add">{count}</button>
    })

    expect(() => rerender(<ScreenNav title="Schedule" />)).not.toThrow()
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })
})
