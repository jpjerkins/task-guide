import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchTasks } from './client'

// The generated `TaskResponse.duration` is `number | string | null` — .NET 10's OpenAPI
// generator describes an int32 as permitting a string on the wire. `client.ts` is the boundary
// that normalises it away. Asserting on the *value* here rather than on rendered text is
// deliberate: a component test cannot tell `30` from `'30'`, because `${x}m` renders both as
// "30m". Only this assertion actually holds the coercion in place.
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
  it('coerces a string duration to a number', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse([{ id: '1', title: 'Water the plants', duration: '30', createdAt: '2026-08-27T10:00:00Z' }]),
      ),
    )

    const [task] = await fetchTasks()

    expect(task.duration).toBe(30)
    expect(typeof task.duration).toBe('number')
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
