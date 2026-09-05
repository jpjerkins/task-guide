import { fireEvent, render, screen } from '@testing-library/react'
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
})
