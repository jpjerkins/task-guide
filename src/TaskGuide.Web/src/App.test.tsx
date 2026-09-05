import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { registerQuickAction, registerScreen, resetRegistry } from './components/shared/screenRegistry'
import { ScreenNav } from './components/shared/ScreenNav'

// Resetting here means the real `screens/tasks.screen.tsx` wiring (App's eager glob runs its
// registerScreen() exactly once, on this file's first import, before this very first beforeEach)
// is never exercised by these tests — every test below registers its own fake screens instead.
// That real wiring is covered by src/screens/tasks.screen.test.tsx, which imports the module
// directly and asserts on its registration before resetting anything.
beforeEach(() => {
  resetRegistry()
})

function goTo(tabLabel: string) {
  fireEvent.click(screen.getByText(tabLabel))
}

// Every screen renders its own ScreenNav (docs/prototypes/ui-screens.prototype.html calls nav()
// from each screen, never from the shell) — these two registrations model that.
function registerTwoScheduleScreens() {
  registerScreen({
    id: 'sched-a',
    tab: 'schedule',
    title: 'Sched A',
    render: () => (
      <>
        <ScreenNav title="Sched A Detail" />
        <div>Sched A Content</div>
      </>
    ),
  })
  registerScreen({ id: 'sched-b', tab: 'schedule', title: 'Sched B', render: () => <div>Sched B Content</div> })
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
    registerTwoScheduleScreens()
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

  // Review finding 4: the shell used to wrap a selected screen in its own ScreenNav + .scroll,
  // so a screen that renders its own ScreenNav (as every screen does — TasksScreen already does,
  // and any future one adopting the pattern will) got two stacked title bars and two quick-action
  // circles once one was registered.
  it('does not double-wrap a selected screen in another ScreenNav', () => {
    registerTwoScheduleScreens()
    render(<App />)

    goTo('Schedule')
    fireEvent.click(screen.getByText('Sched A'))

    expect(screen.getAllByRole('heading')).toHaveLength(1)
    expect(screen.getByRole('heading', { name: 'Sched A Detail' })).toBeInTheDocument()
  })

  // The shell provides the back action via BackProvider/context instead, since it no longer
  // renders a ScreenNav of its own around the selected screen — the screen's own ScreenNav (with
  // no explicit `back` prop) picks it up from context.
  it('provides the back action via context, and it returns to the index', () => {
    registerTwoScheduleScreens()
    render(<App />)

    goTo('Schedule')
    fireEvent.click(screen.getByText('Sched A'))

    expect(screen.getAllByText(/back/i)).toHaveLength(1)
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

  it('renders the registered quick action once in the multi-screen index and once in a selected screen', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)
    registerTwoScheduleScreens()
    render(<App />)

    goTo('Schedule')
    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Sched A'))
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

  it('shows the registered quick action on a single-screen tab, via that screen\'s own ScreenNav', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)
    registerScreen({
      id: 'now-fake',
      tab: 'now',
      title: 'Now Fake',
      render: () => (
        <>
          <ScreenNav title="Now Fake" />
          <div>Now Fake Content</div>
        </>
      ),
    })
    render(<App />)

    goTo('Now')

    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })
})
