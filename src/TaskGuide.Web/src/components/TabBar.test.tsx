import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TabBar } from './TabBar'

describe('TabBar', () => {
  it('renders all four tabs', () => {
    render(<TabBar active="tasks" onChange={() => {}} />)

    expect(screen.getByText('Now')).toBeInTheDocument()
    expect(screen.getByText('Tasks')).toBeInTheDocument()
    expect(screen.getByText('Schedule')).toBeInTheDocument()
    expect(screen.getByText('More')).toBeInTheDocument()
  })
})
