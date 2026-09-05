import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { screensFor } from '../components/shared/screenRegistry'
import './tasks.screen'

// Deliberately does NOT call resetRegistry(): the eager glob in App.tsx that imports every
// screens/*.screen.tsx file runs its module-level registerScreen() call exactly once per test
// file, on first import — after that, resetRegistry() (as App.test.tsx's beforeEach does) wipes
// it permanently for the rest of that file, with nothing left to re-register it. So App.test.tsx
// resetting the registry means the real `tasks` wiring is never exercised there; this file is
// where that gets covered, importing the module directly and asserting on its own registration
// before anything has a chance to reset it.
beforeEach(() => {
  vi.restoreAllMocks()
})

describe('tasks.screen registration', () => {
  it('registers "tasks" on the tasks tab, rendering TasksScreen', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    const descriptors = screensFor('tasks')
    expect(descriptors).toHaveLength(1)
    expect(descriptors[0]).toMatchObject({ id: 'tasks', tab: 'tasks', title: 'Tasks' })

    render(descriptors[0].render())

    expect(await screen.findByRole('heading', { name: 'Tasks' })).toBeInTheDocument()
  })
})
