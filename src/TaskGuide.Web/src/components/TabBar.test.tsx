import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { registerQuickAction, resetRegistry } from './shared/screenRegistry'
import { TabBar } from './TabBar'

beforeEach(() => {
  resetRegistry()
})

describe('TabBar', () => {
  it('renders all four tabs', () => {
    render(<TabBar active="tasks" onChange={() => {}} />)

    expect(screen.getByText('Now')).toBeInTheDocument()
    expect(screen.getByText('Tasks')).toBeInTheDocument()
    expect(screen.getByText('Schedule')).toBeInTheDocument()
    expect(screen.getByText('More')).toBeInTheDocument()
  })

  it('renders nothing in the quick-action slot when nothing is registered', () => {
    render(<TabBar active="tasks" onChange={() => {}} />)

    expect(screen.queryByLabelText('Quick add')).not.toBeInTheDocument()
  })

  it('renders the registered quick action on every tab', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)

    const { rerender } = render(<TabBar active="tasks" onChange={() => {}} />)
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()

    rerender(<TabBar active="now" onChange={() => {}} />)
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })
})
