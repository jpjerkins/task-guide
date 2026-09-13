import { afterEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { screensFor } from '../components/shared/screenRegistry'
import './overrides.screen'

afterEach(() => vi.unstubAllGlobals())
it('registers_Override_a_date_on_the_schedule_tab', async () => {
  const registration = screensFor('schedule').find(value => value.id === 'overrides')
  expect(registration).toMatchObject({ title: 'Override a date', tab: 'schedule' })
  vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) => new Response(JSON.stringify({ date: url.split('/').at(-1), windows: [], events: [], isOverridden: false }))))
  if (!registration) throw new Error('Override screen must be registered')
  render(registration.render())
  expect(await screen.findByRole('button', { name: 'Stamp a whole shape onto this date…' })).toBeInTheDocument()
})
