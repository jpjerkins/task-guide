import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  clearPostpone,
  deferTaskByOffset,
  fetchDimensions,
  fetchTask,
  fetchTasks,
  saveTaskDetails,
  setTaskDuration,
} from './client'

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

describe('fetchTasks with a status filter', () => {
  it('requests the unprocessed status filter', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await fetchTasks('unprocessed')

    expect(fetchMock).toHaveBeenCalledWith('/api/tasks?status=unprocessed')
  })

  it('requests the stale status filter', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await fetchTasks('stale')

    expect(fetchMock).toHaveBeenCalledWith('/api/tasks?status=stale')
  })
})

describe('setTaskDuration', () => {
  it('PUTs the bucket to the duration endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await setTaskDuration('t1', '30')

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/tasks/t1/duration',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ duration: '30' }),
      }),
    )
  })
})

function rawTaskDetail(overrides: Record<string, unknown> = {}) {
  return {
    id: '1',
    title: 'Water the plants',
    notes: null,
    duration: null,
    dimensions: {},
    looseTags: [],
    createdAt: '2026-08-27T10:00:00Z',
    status: 'active',
    eligible: true,
    deadline: null,
    defer: null,
    postpone: null,
    recurring: false,
    derived: false,
    opportunities: null,
    patternWeekCount: null,
    zeroKind: null,
    orphanBlameDimensions: [],
    ...overrides,
  }
}

describe('fetchTask', () => {
  it('normalises opportunities and patternWeekCount from the wire int32-as-string/number artifact', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(rawTaskDetail({ opportunities: '3', patternWeekCount: 5 }))),
    )

    const task = await fetchTask('1')

    expect(task?.opportunities).toBe(3)
    expect(task?.patternWeekCount).toBe(5)
  })

  it('leaves a null opportunities/patternWeekCount null rather than coercing to 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(rawTaskDetail({ opportunities: null, patternWeekCount: null }))),
    )

    const task = await fetchTask('1')

    expect(task?.opportunities).toBeNull()
    expect(task?.patternWeekCount).toBeNull()
  })

  it('carries notes, dimensions, looseTags and orphanBlameDimensions through', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          rawTaskDetail({
            notes: 'a note',
            dimensions: { location: ['home'] },
            looseTags: ['errand'],
            orphanBlameDimensions: ['location'],
          }),
        ),
      ),
    )

    const task = await fetchTask('1')

    expect(task?.notes).toBe('a note')
    expect(task?.dimensions).toEqual({ location: ['home'] })
    expect(task?.looseTags).toEqual(['errand'])
    expect(task?.orphanBlameDimensions).toEqual(['location'])
  })

  it('returns null on a 204 (absence)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    const task = await fetchTask('1')

    expect(task).toBeNull()
  })

  it('throws on a 404, for the screen error arm to catch', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))

    await expect(fetchTask('missing')).rejects.toThrow()
  })
})

describe('saveTaskDetails', () => {
  it('PUTs the whole form to /api/tasks/{id}', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await saveTaskDetails('1', {
      title: 'New title',
      notes: 'notes',
      duration: '30',
      deadline: '2026-10-01',
      dimensions: { location: ['home'] },
    })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/tasks/1',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          title: 'New title',
          notes: 'notes',
          duration: '30',
          deadline: '2026-10-01',
          dimensions: { location: ['home'] },
        }),
      }),
    )
  })
})

describe('clearPostpone', () => {
  it('DELETEs /api/tasks/{id}/postpone', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await clearPostpone('1')

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/tasks/1/postpone',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})

describe('deferTaskByOffset', () => {
  it('PATCHes /api/tasks/{id} with a null date and the given offset/unit', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await deferTaskByOffset('1', 3, 1)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/tasks/1',
      expect.objectContaining({
        method: 'PATCH',
        body: JSON.stringify({ date: null, offset: 3, unit: 1 }),
      }),
    )
  })
})
