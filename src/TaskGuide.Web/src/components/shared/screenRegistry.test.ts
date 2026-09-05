import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  installHmrGuard,
  quickAction,
  registerQuickAction,
  registerScreen,
  resetRegistry,
  screensFor,
} from './screenRegistry'

beforeEach(() => {
  resetRegistry()
})

describe('screensFor', () => {
  it('orders screens by order then id', () => {
    registerScreen({ id: 'z-screen', tab: 'schedule', title: 'Z', order: 1, render: () => null })
    registerScreen({ id: 'a-screen', tab: 'schedule', title: 'A', order: 1, render: () => null })
    registerScreen({ id: 'b-screen', tab: 'schedule', title: 'B', order: 0, render: () => null })

    const ids = screensFor('schedule').map((s) => s.id)

    expect(ids).toEqual(['b-screen', 'a-screen', 'z-screen'])
  })

  it('returns an empty list for a tab with nothing registered', () => {
    registerScreen({ id: 'sched-screen', tab: 'schedule', title: 'Sched', render: () => null })
    registerScreen({ id: 'tasks-screen', tab: 'tasks', title: 'Tasks', render: () => null })

    expect(screensFor('now')).toEqual([])
  })

  it('does not return screens registered for a different tab', () => {
    registerScreen({ id: 'now-screen', tab: 'now', title: 'Now', render: () => null })

    expect(screensFor('tasks')).toEqual([])
  })
})

describe('registerScreen', () => {
  it('throws on a duplicate id', () => {
    registerScreen({ id: 'dup', tab: 'now', title: 'First', render: () => null })

    expect(() => registerScreen({ id: 'dup', tab: 'schedule', title: 'Second', render: () => null })).toThrow(
      /duplicate screen id "dup"/,
    )
  })
})

describe('quick action slot', () => {
  it('is null when nothing has registered', () => {
    expect(quickAction()).toBeNull()
  })

  it('returns the registered renderer', () => {
    const render = () => null
    registerQuickAction(render)

    expect(quickAction()).toBe(render)
  })

  it('throws on a second registration', () => {
    registerQuickAction(() => null)

    expect(() => registerQuickAction(() => null)).toThrow(/quick action is already registered/)
  })
})

// Review finding 5: screens/tasks.screen.tsx exports nothing, so it is not its own Fast Refresh
// boundary — the HMR update propagates to App.tsx (which owns the eager glob), and when App.tsx
// re-executes the glob, `registerScreen` throws "duplicate screen id" against a registry that was
// never cleared. App.tsx's own dispose handler (installHmrGuard(import.meta.hot, resetRegistry))
// is what actually fixes this in dev; this test holds only the guard's own logic, since
// import.meta.hot isn't something a unit test can fake convincingly end-to-end.
describe('installHmrGuard', () => {
  it('registers a dispose handler that resets the registry, when hot is present', () => {
    const dispose = vi.fn()

    installHmrGuard({ dispose }, resetRegistry)

    expect(dispose).toHaveBeenCalledWith(resetRegistry)
  })

  it('does nothing when hot is undefined', () => {
    expect(() => installHmrGuard(undefined, resetRegistry)).not.toThrow()
  })
})
