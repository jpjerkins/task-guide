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

  it('removes only the disposed hot screen before it re-registers', () => {
    let disposeCallback: (() => void) | undefined
    const dispose = vi.fn((callback: () => void) => {
      disposeCallback = callback
    })
    const hot = { dispose }

    registerScreen({ id: 'updated', tab: 'now', title: 'Updated', render: () => null }, hot)
    registerScreen({ id: 'sibling', tab: 'now', title: 'Sibling', render: () => null })
    disposeCallback?.()

    expect(dispose).toHaveBeenCalledOnce()
    expect(screensFor('now').map((screen) => screen.id)).toEqual(['sibling'])
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

// An App.tsx update cannot safely rebuild the eager screen imports from the browser module cache.
// The guard deliberately invalidates it so Vite performs a full reload. This test covers that
// wiring only; it cannot reproduce Vite's module-graph invalidation end-to-end.
describe('installHmrGuard', () => {
  it('registers a dispose handler that invalidates App.tsx, when hot is present', () => {
    const dispose = vi.fn()
    const invalidate = vi.fn()

    installHmrGuard({ dispose, invalidate })

    expect(dispose).toHaveBeenCalledOnce()
    dispose.mock.calls[0][0]()
    expect(invalidate).toHaveBeenCalledOnce()
  })

  it('does nothing when hot is undefined', () => {
    expect(() => installHmrGuard(undefined)).not.toThrow()
  })
})
