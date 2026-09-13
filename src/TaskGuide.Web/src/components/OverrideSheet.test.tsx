import { render, screen, fireEvent } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { OverrideSheet } from './OverrideSheet'

it('an_Override_sheet_takes_focus_and_Escape_cancels_without_changing_its_content', () => {
  const cancel = vi.fn()
  render(<OverrideSheet title="Test shape" onCancel={cancel}><input aria-label="Name" /></OverrideSheet>)
  const sheet = screen.getByRole('dialog', { name: 'Test shape' })
  expect(sheet).toHaveFocus()
  fireEvent.keyDown(sheet, { key: 'Escape' })
  expect(cancel).toHaveBeenCalledOnce()
})

it('Tab_stays_inside_an_Override_sheet_and_busy_sheets_cannot_be_cancelled', () => {
  const cancel = vi.fn()
  const { rerender } = render(<OverrideSheet title="Test shape" onCancel={cancel}><input aria-label="Name" /></OverrideSheet>)
  const input = screen.getByLabelText('Name')
  input.focus()
  fireEvent.keyDown(input, { key: 'Tab' })
  expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  fireEvent.keyDown(screen.getByRole('button', { name: 'Cancel' }), { key: 'Tab', shiftKey: true })
  expect(input).toHaveFocus()
  rerender(<OverrideSheet title="Test shape" onCancel={cancel} busy><input aria-label="Name" /></OverrideSheet>)
  fireEvent.keyDown(input, { key: 'Escape' })
  fireEvent.click(screen.getByRole('dialog'))
  expect(cancel).not.toHaveBeenCalled()
})
