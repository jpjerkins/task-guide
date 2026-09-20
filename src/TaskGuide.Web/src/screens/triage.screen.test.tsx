import { render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { screensFor } from '../components/shared/screenRegistry'
import './triage.screen'

afterEach(() => vi.unstubAllGlobals())

it('registers "Process & stale" on the More tab', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 })))

  const registration = screensFor('more').find((value) => value.id === 'triage')
  expect(registration).toMatchObject({ id: 'triage', tab: 'more', title: 'Process & stale' })
  if (!registration) throw new Error('Triage screen must be registered')

  render(registration.render())

  expect(await screen.findByRole('heading', { name: 'Process' })).toBeInTheDocument()
})
