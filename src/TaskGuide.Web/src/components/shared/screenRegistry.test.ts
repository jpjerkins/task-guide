import { beforeEach, describe, expect, it } from 'vitest'
import { quickAction, registerQuickAction, registerScreen, resetRegistry, screensFor } from './screenRegistry'

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
