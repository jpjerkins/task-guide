import { act, fireEvent, render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useOverrideConfirmation } from './OverrideConfirmation'

// A minimal host so the hook's presentation renders where a real caller (OverrideScreen) would
// mount it. confirm() is stashed on window so tests can invoke it outside the render tree.
function Host() {
  const { confirm, presentation } = useOverrideConfirmation()
  ;(window as unknown as { confirm: typeof confirm }).confirm = confirm
  return <>{presentation}</>
}

it('the_title_and_confirm_button_carry_both_counts_when_the_span_exceeds_the_clobbered_count', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03', '2026-11-04', '2026-11-07'], 12) })
  expect(await screen.findByRole('heading', { name: 'Replace 3 Overrides?' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Replace 3 and stamp all 12' })).toBeInTheDocument()
})

it('the_title_is_singular_for_a_single_clobbered_date', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03'], 5) })
  expect(await screen.findByRole('heading', { name: 'Replace 1 Override?' })).toBeInTheDocument()
})

it('the_confirm_button_reads_Replace_all_n_when_the_whole_span_is_clobbered', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03', '2026-11-04'], 2) })
  expect(await screen.findByRole('button', { name: 'Replace all 2' })).toBeInTheDocument()
})

it('the_list_is_a_damage_block_whose_dates_render_through_fmtShort_with_an_already-an-override_pill_and_the_note_states_the_untouched_count', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03', '2026-11-04'], 12) })
  await screen.findByRole('heading', { name: 'Replace 2 Overrides?' })
  const damage = document.querySelector('.damage')
  expect(damage).not.toBeNull()
  expect(damage).toHaveTextContent('2 of the 12 dates in this span already depart from the pattern')
  const rows = damage!.querySelectorAll('.row')
  expect(rows).toHaveLength(2)
  expect(rows[0]).toHaveTextContent('Tue 3 Nov')
  expect(rows[0].querySelector('.title')).not.toHaveTextContent('2026-11-03')
  expect(rows[0].querySelector('.pill.due')).toHaveTextContent('already an override')
  expect(document.querySelector('.note')).toHaveTextContent('The other 10 dates are following the pattern and will be copied off it.')
  expect(document.querySelector('.note')).toHaveTextContent('Nothing here can be undone in one step — reverting is per date.')
})

// #140 review finding 4: `untouched === 1` fell through to the plural form ("The other 1 dates
// are following the pattern"), the same singular/plural miss `n` already gets on the title.
it('the_untouched-dates_sentence_is_singular_for_exactly_one_untouched_date', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03', '2026-11-04'], 3) })
  await screen.findByRole('heading', { name: 'Replace 2 Overrides?' })
  expect(document.querySelector('.note')).toHaveTextContent('The other 1 date is following the pattern and will be copied off it.')
})

it('drops_the_other-dates_sentence_when_the_whole_span_is_clobbered_but_keeps_the_undo_sentence', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03', '2026-11-04'], 2) })
  await screen.findByRole('heading', { name: 'Replace 2 Overrides?' })
  const note = document.querySelector('.note')
  expect(note).not.toHaveTextContent('The other 0 dates')
  expect(note).toHaveTextContent('Nothing here can be undone in one step — reverting is per date.')
})

it('confirming_resolves_true_and_cancelling_resolves_false_and_neither_adds_a_second_cancel', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  let result: Promise<boolean> = Promise.resolve(false)
  act(() => { result = confirm(['2026-11-03'], 5) })
  await screen.findByRole('heading', { name: 'Replace 1 Override?' })
  expect(screen.getAllByRole('button', { name: 'Cancel' })).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Replace 1 and stamp all 5' }))
  expect(await result).toBe(true)
})

// The commonest clobber path by far: stamping the single date you are looking at, when it already
// carries an Override. "Replace all 1" is not English, and "1 of the 1 dates in this span" is worse.
it('a_single_date_that_is_its_own_whole_span_reads_as_one_date_not_as_a_span_of_one', async () => {
  render(<Host />)
  const confirm = (window as unknown as { confirm: (affected: readonly string[], span: number) => Promise<boolean> }).confirm
  act(() => { void confirm(['2026-11-03'], 1) })
  expect(await screen.findByRole('button', { name: 'Replace it' })).toBeInTheDocument()
  expect(document.querySelector('.damage-h')).toHaveTextContent(
    'This date already departs from the pattern. Stamping replaces what is on it.')
})
