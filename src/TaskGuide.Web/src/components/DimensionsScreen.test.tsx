import { render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DimensionsScreen } from './DimensionsScreen'

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

// Finds the .row that directly owns a .title with this exact text — proves the label is
// nested inside its own row, not merely present somewhere on the page.
function getRow(container: HTMLElement, label: string): HTMLElement {
  const titles = Array.from(container.querySelectorAll('.title')).filter(
    (element) => element.textContent === label,
  )
  expect(titles).toHaveLength(1)
  const row = titles[0].closest('.row')
  expect(row).toBeInTheDocument()
  return row as HTMLElement
}

// The always-present .meta direct child of .body (as opposed to the categorical value
// container, which is also a .meta but carries the extra .chipset class).
function getMeta(body: HTMLElement): HTMLElement {
  const meta = body.querySelector(':scope > .meta:not(.chipset)')
  expect(meta).toBeInTheDocument()
  return meta as HTMLElement
}

function getSourcePill(meta: HTMLElement): HTMLElement {
  const pills = meta.querySelectorAll(':scope > .pill.dim')
  expect(pills.length).toBeGreaterThan(0)
  return pills[0] as HTMLElement
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('DimensionsScreen', () => {
  it('renders every registry Dimension in order with its values', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([
          {
            id: 'location',
            label: 'Location',
            algebra: 'categorical',
            values: ['home', 'garage'],
            taskDefault: null,
            windowDefault: null,
            source: 'authored',
          },
          {
            id: 'energy',
            label: 'Mental energy',
            algebra: 'ordinal',
            values: ['low', 'medium', 'high'],
            taskDefault: 'low',
            windowDefault: 'low',
            source: 'authored',
          },
        ]),
      ),
    )

    const { container } = render(<DimensionsScreen />)

    expect(await screen.findByRole('heading', { name: 'Dimensions' })).toBeInTheDocument()
    expect(container.querySelector('.scroll > .list')).toBeInTheDocument()
    const titles = Array.from(container.querySelectorAll('.list .row .title')).map((element) => element.textContent)
    expect(titles).toEqual(['Location', 'Mental energy'])
    expect(screen.getByText('home')).toBeInTheDocument()
    expect(screen.getByText('garage')).toBeInTheDocument()
    expect(screen.getByText('low')).toBeInTheDocument()
    expect(screen.getByText('medium')).toBeInTheDocument()
    expect(screen.getByText('high')).toBeInTheDocument()

    const locationRow = getRow(container, 'Location')
    const energyRow = getRow(container, 'Mental energy')

    for (const [row, label] of [
      [locationRow, 'Location'],
      [energyRow, 'Mental energy'],
    ] as const) {
      const body = row.querySelector(':scope > .body')
      expect(body).toBeInTheDocument()
      expect(body!.querySelector(':scope > .title')).toHaveTextContent(label)
      const meta = getMeta(body as HTMLElement)
      // Both rows are authored: the source pill reads "authored" and no "fetched" text
      // leaks into this row's scope.
      expect(getSourcePill(meta)).toHaveTextContent('authored')
      expect(within(row).queryByText('fetched')).not.toBeInTheDocument()
    }

    // Categorical row: the second .meta (.meta.chipset) holds one .pill per value, no buttons.
    const locationBody = locationRow.querySelector(':scope > .body') as HTMLElement
    const locationChipset = locationBody.querySelector(':scope > .meta.chipset')
    expect(locationChipset).toBeInTheDocument()
    const locationPills = locationChipset!.querySelectorAll(':scope > .pill')
    expect(Array.from(locationPills).map((pill) => pill.textContent)).toEqual(['home', 'garage'])
    expect(locationChipset!.querySelectorAll('button')).toHaveLength(0)

    // Ordinal row: the third direct child of .body is OrdinalSlider's .stack root, and the
    // slider found within it belongs to this row (not just floating unscoped on the page).
    const energyBody = energyRow.querySelector(':scope > .body') as HTMLElement
    const energyStack = energyBody.querySelector(':scope > .stack')
    expect(energyStack).toBeInTheDocument()
    const energySlider = within(energyRow).getByRole('slider', { name: 'Mental energy' })
    expect(energyStack).toContainElement(energySlider)

    const notes = container.querySelectorAll('.scroll > .note')
    expect(notes).toHaveLength(2)
    expect(notes[0]).toHaveTextContent(/two algebras/i)
    expect(notes[0].querySelector('b')).toHaveTextContent('Ordinal')
    expect(notes[1]).toHaveTextContent(/no ui for managing dimensions/i)
  })

  it('presents ordinal values through a disabled shared slider and categorical values without controls', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([
          {
            id: 'location',
            label: 'Location',
            algebra: 'categorical',
            values: ['home', 'garage'],
            taskDefault: null,
            windowDefault: null,
            source: 'authored',
          },
          {
            id: 'energy',
            label: 'Mental energy',
            algebra: 'ordinal',
            values: ['low', 'medium', 'high'],
            taskDefault: 'low',
            windowDefault: 'low',
            source: 'authored',
          },
          {
            id: 'weather',
            label: 'Weather',
            algebra: 'categorical',
            values: ['dry', 'wet'],
            taskDefault: null,
            windowDefault: null,
            source: 'fetched',
          },
        ]),
      ),
    )

    const { container } = render(<DimensionsScreen />)

    await screen.findByRole('heading', { name: 'Dimensions' })

    const locationRow = getRow(container, 'Location')
    const energyRow = getRow(container, 'Mental energy')
    const weatherRow = getRow(container, 'Weather')

    // Authored rows: own source pill reads "authored", and neither carries the "fetched" marker.
    for (const row of [locationRow, energyRow]) {
      const body = row.querySelector(':scope > .body') as HTMLElement
      const meta = getMeta(body)
      expect(getSourcePill(meta)).toHaveTextContent('authored')
      expect(within(row).queryByText('fetched')).not.toBeInTheDocument()
    }

    // The fetched row is the sole owner of the "fetched" marker.
    const weatherBody = weatherRow.querySelector(':scope > .body') as HTMLElement
    const weatherMeta = getMeta(weatherBody)
    expect(getSourcePill(weatherMeta)).toHaveTextContent('fetched')
    expect(within(weatherRow).getByText('fetched')).toBeInTheDocument()

    // Ordinal row: .stack is the direct value-container child, and the slider within it is the
    // one under test — disabled, dimmed, with three ticks and a default-related hint.
    const energyBody = energyRow.querySelector(':scope > .body') as HTMLElement
    const energyStack = energyBody.querySelector(':scope > .stack')
    expect(energyStack).toBeInTheDocument()
    const slider = within(energyRow).getByRole('slider', { name: 'Mental energy' })
    expect(energyStack).toContainElement(slider)
    expect(slider).toBeDisabled()
    expect(slider).toHaveClass('range', 'unset')
    expect(energyStack!.querySelectorAll(':scope > .ticks > span')).toHaveLength(3)
    expect(energyStack!.querySelector(':scope > .hint')).toHaveTextContent(/default/i)

    const defaultControl = within(energyRow).getByRole('button', { name: /leave at the default \(low\)/i })
    expect(defaultControl).toBeDisabled()
    expect(defaultControl).toHaveAttribute('aria-pressed', 'true')

    // Categorical rows (Location, Weather): .meta.chipset holds exactly the declared values,
    // in order, and zero buttons.
    for (const [row, values] of [
      [locationRow, ['home', 'garage']],
      [weatherRow, ['dry', 'wet']],
    ] as const) {
      const body = row.querySelector(':scope > .body') as HTMLElement
      const chipset = body.querySelector(':scope > .meta.chipset')
      expect(chipset).toBeInTheDocument()
      const pills = chipset!.querySelectorAll(':scope > .pill')
      expect(Array.from(pills).map((pill) => pill.textContent)).toEqual(values)
      expect(chipset!.querySelectorAll('button')).toHaveLength(0)
    }
  })

  it('renders an empty state when the registry is empty', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))

    const { container } = render(<DimensionsScreen />)

    expect(await screen.findByText('No dimensions are declared.')).toBeInTheDocument()
    expect(container.querySelector('.list')).not.toBeInTheDocument()
  })
})
