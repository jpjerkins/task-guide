import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchDimensions, fetchTasks } from './client'

// The generated `TaskResponse.duration` is `number | string | null` — .NET 10's OpenAPI
// generator describes an int32 as permitting a string on the wire. `client.ts` is the boundary
// that normalises every Duration bucket to its string form. Asserting on the *value* here rather
// than on rendered text is deliberate: a component test cannot tell `30` from `'30'`, because
// `${x}m` renders both as "30m" — and only a value assertion distinguishes a non-numeric bucket
// like `'longer'` surviving from it becoming `NaN`.
beforeEach(() => {
  vi.restoreAllMocks()
})

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('fetchTasks', () => {
  it('carries a numeric duration bucket through as its string form', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([{ id: '1', title: 'Water the plants', duration: 30, createdAt: '2026-08-27T10:00:00Z' }]),
      ),
    )

    const [task] = await fetchTasks()

    expect(task.duration).toBe('30')
    expect(typeof task.duration).toBe('string')
  })

  it('carries a non-numeric duration bucket through verbatim', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([{ id: '1', title: 'Water the plants', duration: 'longer', createdAt: '2026-08-27T10:00:00Z' }]),
      ),
    )

    const [task] = await fetchTasks()

    expect(task.duration).toBe('longer')
  })

  it('leaves a null duration null rather than coercing it to 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([{ id: '1', title: 'Water the plants', duration: null, createdAt: '2026-08-27T10:00:00Z' }]),
      ),
    )

    const [task] = await fetchTasks()

    expect(task.duration).toBeNull()
  })
})

describe('fetchDimensions', () => {
  it('passes a normal array payload through unchanged', async () => {
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
        ]),
      ),
    )

    const dimensions = await fetchDimensions()

    expect(dimensions).toEqual([
      {
        id: 'location',
        label: 'Location',
        algebra: 'categorical',
        values: ['home', 'garage'],
        taskDefault: null,
        windowDefault: null,
        source: 'authored',
      },
    ])
  })

  it('normalises a 204/null response to an empty array', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    const dimensions = await fetchDimensions()

    expect(dimensions).toEqual([])
  })
})
