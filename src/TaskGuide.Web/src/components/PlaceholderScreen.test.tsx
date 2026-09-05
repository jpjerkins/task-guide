import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { registerQuickAction, resetRegistry } from './shared/screenRegistry'
import { PlaceholderScreen } from './PlaceholderScreen'

beforeEach(() => {
  resetRegistry()
})

describe('PlaceholderScreen', () => {
  it('renders the title and the not-built-yet message', () => {
    render(<PlaceholderScreen title="More" />)

    expect(screen.getByRole('heading', { name: 'More' })).toBeInTheDocument()
    expect(screen.getByText(/not built yet/i)).toBeInTheDocument()
  })

  it('renders the registered quick action, since a placeholder tab is a screen too', () => {
    registerQuickAction(() => <button aria-label="Quick add">+</button>)

    render(<PlaceholderScreen title="More" />)

    expect(screen.getByLabelText('Quick add')).toBeInTheDocument()
  })
})
