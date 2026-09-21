import type { components } from './schema'

// The one normalisation boundary for the wire. `getJson`/`sendJson` encapsulate the request
// policy every resource shares — non-OK throws, a 204 reads as "nothing to parse", JSON parses
// otherwise — so a later ticket adding a resource writes a thin normaliser on top of these
// instead of re-deriving fetch policy. Nothing about a wire *type* is hand-written anywhere:
// those come from `components['schemas'][...]` in the generated schema.d.ts. A refused write's
// typed reason (`{ error: string }`, #138) is part of that policy too — it's carried on
// `ApiError` rather than dropped, so a caller can render it.

// Thrown by `sendJson` on a non-OK response. `reason` is the server's `error` string when the
// body parsed as one, null otherwise (no body, or a body that isn't `{ error: string }`).
export class ApiError extends Error {
  readonly status: number
  readonly reason: string | null

  constructor(message: string, status: number, reason: string | null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.reason = reason
  }
}

export async function getJson<T>(path: string): Promise<T | null> {
  const res = await fetch(path)
  if (!res.ok) {
    throw new Error(`GET ${path} failed: ${res.status}`)
  }
  // The API currently answers every endpoint with 204 No Content while the store is
  // being wired up — treat "nothing to parse" as absence rather than a parse error.
  if (res.status === 204) {
    return null
  }
  return (await res.json()) as T
}

export async function sendJson<T>(method: string, path: string, body: unknown): Promise<T | null> {
  const res = await fetch(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    let reason: string | null = null
    try {
      const parsed: unknown = JSON.parse(await res.text())
      if (parsed !== null && typeof parsed === 'object' && typeof (parsed as { error?: unknown }).error === 'string') {
        reason = (parsed as { error: string }).error
      }
    } catch {
      // No body, or not JSON — reason stays null.
    }
    throw new ApiError(`${method} ${path} failed: ${res.status}`, res.status, reason)
  }
  if (res.status === 204) {
    return null
  }
  return (await res.json()) as T
}

// Raw wire shapes, straight from the generated OpenAPI schema.
type TaskResponse = components['schemas']['TaskResponse']
type CreateTaskRequest = components['schemas']['CreateTaskRequest']

// App-facing Task: `duration` normalised to `string | null`. A Duration is a *bucket*
// (KnownDimensions.DurationBuckets: "2" | "10" | "30" | "60" | "longer"), not a minute count.
// The generated `TaskResponse.duration` is typed `number | string | null` — an artifact of how
// .NET 10's OpenAPI generator describes an int32, which is what the wire still carries: the
// server sends a JSON number and cannot yet send `longer` (TaskResponse.Duration is `int?`
// until #130 widens it). So this boundary is deliberately ahead of the server — normalising
// every bucket to its string form is a no-op on today's payloads, and stops `longer` becoming
// `NaN` the moment it starts arriving.
export interface Task {
  id: string
  title: string
  duration: string | null
  createdAt: string
  // #163: status/eligible/deadline/defer/postpone/recurring/derived/zeroKind pass through as-is —
  // the wire already sends camelCase strings and booleans, so there's nothing to normalise.
  // opportunities and patternWeekCount aren't here: nothing reads them yet (#175 will), and
  // adding them ahead of a reader would mean adding `toNumber` untested against a real caller —
  // YAGNI, add them when a screen needs them.
  status: string
  eligible: boolean
  deadline: string | null
  defer: string | null
  postpone: string | null
  recurring: boolean
  derived: boolean
  zeroKind: string | null
}

export type NewTask = Pick<CreateTaskRequest, 'title'> & { duration: number }

function toTask(raw: TaskResponse): Task {
  return {
    id: raw.id,
    title: raw.title,
    duration: raw.duration === null || raw.duration === undefined ? null : String(raw.duration),
    createdAt: raw.createdAt,
    status: raw.status,
    eligible: raw.eligible,
    deadline: raw.deadline,
    defer: raw.defer,
    postpone: raw.postpone,
    recurring: raw.recurring,
    derived: raw.derived,
    zeroKind: raw.zeroKind,
  }
}

export async function fetchTasks(status?: 'unprocessed' | 'stale'): Promise<Task[]> {
  const path = status ? `/api/tasks?status=${status}` : '/api/tasks'
  const raw = await getJson<TaskResponse[]>(path)
  return raw === null ? [] : raw.map(toTask)
}

export async function setTaskDuration(id: string, duration: string): Promise<void> {
  await sendJson('PUT', `/api/tasks/${id}/duration`, { duration })
}

export async function createTask(task: NewTask): Promise<void> {
  await sendJson<TaskResponse>('POST', '/api/tasks', task)
}

type DimensionResponse = components['schemas']['DimensionResponse']
export type { DimensionResponse }

export async function fetchDimensions(): Promise<DimensionResponse[]> {
  const raw = await getJson<DimensionResponse[]>('/api/dimensions')
  return raw === null ? [] : raw
}
