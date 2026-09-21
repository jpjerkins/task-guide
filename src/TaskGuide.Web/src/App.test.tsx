import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { registerQuickAction, registerScreen, resetRegistry, usePush } from './components/shared/screenRegistry'
import { ScreenNav } from './components/shared/ScreenNav'

// Resetting here means the real `screens/tasks.screen.tsx` wiring (App's eager glob runs its
// registerScreen() exactly once, on this file's first import, before this very first beforeEach)
// is never exercised by these tests — every test below registers its own fake screens instead.
// That real wiring is covered by src/screens/tasks.screen.test.tsx, which imports the module
// directly and asserts on its registration before resetting anything.
beforeEach(() => {
  resetRegistry()
})

afterEach(() => {
  vi.unstubAllGlobals()
  window.history.replaceState({}, '', '/')
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
  it.each([
    ['/2026-09-13/w_evening', '/api/reminders/2026-09-13/w_evening'],
    ['/2026-09-13/fallback', '/api/reminders/2026-09-13/fallback'],
  ])('cold-loads %s into ReminderPage with its route identity', async (route, apiPath) => {
    window.history.replaceState({}, '', route)
    const fetchMock = vi.fn(async () => new Response(null, { status: 404 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(apiPath))
  })

  it('renders the reminder route with no tab bar — a cold notification link has no tabs to return to', async () => {
    window.history.replaceState({}, '', '/2026-09-13/w_evening')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 404 })))

    render(<App />)

    expect(document.querySelector('.tabbar')).toBeNull()
  })

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
  // so a screen that renders its own ScreenNav got two stacked title bars and two quick-action
  // circles once one was registered. TasksScreen still hand-rolls its nav; the registrations here
  // model screens that adopt the shared component.
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

  describe('screen push', () => {
    function registerPushingScreen() {
      registerScreen({
        id: 'now-fake',
        tab: 'now',
        title: 'Now Fake',
        render: () => <PushingScreen />,
      })
    }

    function PushingScreen() {
      const push = usePush()
      return (
        <button
          onClick={() =>
            push({
              node: (
                <>
                  <ScreenNav title="Pushed" />
                  <div>Pushed Content</div>
                </>
              ),
              backLabel: 'Now Fake',
            })
          }
        >
          Push it
        </button>
      )
    }

    it("a task row opens that task's detail as a pushed screen, with a back control to the list", () => {
      registerPushingScreen()
      render(<App />)

      goTo('Now')
      fireEvent.click(screen.getByText('Push it'))

      expect(screen.getByText('Pushed Content')).toBeInTheDocument()
      expect(screen.getByText(/now fake/i)).toBeInTheDocument()
    })

    it('the pushed screen replaces the tab\'s own screen and the tab bar stays — it is a push, not a route', () => {
      registerPushingScreen()
      render(<App />)

      goTo('Now')
      fireEvent.click(screen.getByText('Push it'))

      expect(screen.queryByText('Push it')).not.toBeInTheDocument()
      expect(document.querySelector('.tabbar')).not.toBeNull()
    })

    it('switching tabs drops the pushed screen, so a tab never reopens someone else\'s detail', () => {
      registerPushingScreen()
      render(<App />)

      goTo('Now')
      fireEvent.click(screen.getByText('Push it'))
      expect(screen.getByText('Pushed Content')).toBeInTheDocument()

      goTo('More')
      goTo('Now')

      expect(screen.queryByText('Pushed Content')).not.toBeInTheDocument()
      expect(screen.getByText('Push it')).toBeInTheDocument()
    })
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
