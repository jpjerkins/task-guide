import { afterEach, expect, it } from 'vitest'
import { cleanup, render } from '@testing-library/react'
import { dimPills } from './OverrideDimPills'

afterEach(() => cleanup())

it('a_dimension_whose_values_are_all_null_or_absent_renders_no_pill_at_all', () => {
  const { container } = render(<div>{dimPills({ dimensions: { Energy: [{ value: null }] }, looseTags: [] })}</div>)
  expect(container.querySelector('.pill.dim')).toBeNull()
})

it('a_dimension_with_a_mix_of_real_and_null_values_joins_only_the_real_ones', () => {
  const { container } = render(<div>{dimPills({ dimensions: { Location: [{ value: 'foo' }, { value: null }, { value: 'bar' }] }, looseTags: [] })}</div>)
  expect(container.querySelector('.pill.dim')).toHaveTextContent('location: foo / bar')
})

it('a_loose_tag_with_an_absent_value_renders_no_inert_pill_at_all', () => {
  const { container } = render(<div>{dimPills({ dimensions: {}, looseTags: [{}] })}</div>)
  expect(container.querySelector('.pill.inert')).toBeNull()
})
