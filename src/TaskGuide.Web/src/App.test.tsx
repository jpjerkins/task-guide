import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { registerQuickAction, registerScreen, resetRegistry } from './components/shared/screenRegistry'

beforeEach(() => {
  resetRegistry()
})

function goTo(tabLabel: string) {
  fireEvent.click(screen.getByText(tabLabel))
}

describe('App', () => {
  it('renders the placeholder for a tab with no registered screen', () => {
    render(<App />)

    goTo('More')

    expect(screen.getByText(/not built yet/i)).toBeInTheDocument()
  })

  it('renders a single registered screen directly', () => {
    registerScreen({ id: 'now-fake', tab: 'now', title: 'Now Fake', render: () => <div>Now Fake Content</div> })
    render(<App />)

    goTo('Now')

    expect(screen.getByText('Now Fake Content')).toBeInTheDocument()
  })

  it('renders an index for a tab with more than one registered screen, and navigates back', () => {
    registerScreen({ id: 'sched-a', tab: 'schedule', title: 'Sched A', render: () => <div>Sched A Content</div> })
    registerScreen({ id: 'sched-b', tab: 'schedule', title: 'Sched B', render: () => <div>Sched B Content</div> })
    render(<App />)

    goTo('Schedule')

    expect(screen.getByText('Sched A')).toBeInTheDocument()
    expect(screen.getByText('Sched B')).toBeInTheDocument()
    expect(screen.queryByText('Sched A Content')).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Sched A'))
    expect(screen.getByText('Sched A Content')).toBeInTheDocument()

    fireEvent.click(screen.getByText(/back/i))
    expect(screen.getByText('Sched A')).toBeInTheDocument()
    expect(screen.getByText('Sched B')).toBeInTheDocument()
  })

  it('picks up a freshly registered screen with zero changes to App.tsx', () => {
    registerScreen({ id: 'brand-new', tab: 'more', title: 'Brand New', render: () => <div>Brand New Content</div> })
    render(<App />)

    goTo('More')

    expect(screen.getByText('Brand New Content')).toBeInTheDocument()
  })

  it('renders the registered quick action in the multi-screen index and in a selected screen', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)
    registerScreen({ id: 'sched-a2', tab: 'schedule', title: 'Sched A2', render: () => <div>Sched A2 Content</div> })
    registerScreen({ id: 'sched-b2', tab: 'schedule', title: 'Sched B2', render: () => <div>Sched B2 Content</div> })
    render(<App />)

    goTo('Schedule')
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Sched A2'))
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })

  it('renders the registered quick action on more than one active tab, including a placeholder tab', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)
    render(<App />)

    // 'More' has no registered screen — a placeholder tab is a screen too.
    goTo('More')
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()

    goTo('Now')
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })
})
