import { render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { screensFor } from '../components/shared/screenRegistry'
import './dimensions.screen'

afterEach(() => vi.unstubAllGlobals())

it('registers Dimensions on the More tab', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 })))

  const registration = screensFor('more').find((value) => value.id === 'dimensions')
  expect(registration).toMatchObject({ id: 'dimensions', tab: 'more', title: 'Dimensions' })
  if (!registration) throw new Error('Dimensions screen must be registered')

  render(registration.render())

  expect(await screen.findByRole('heading', { name: 'Dimensions' })).toBeInTheDocument()
})
