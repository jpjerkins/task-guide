import type { components } from './schema'

// The one normalisation boundary for the wire. `getJson`/`sendJson` encapsulate the request
// policy every resource shares — non-OK throws, a 204 reads as "nothing to parse", JSON parses
// otherwise — so a later ticket adding a resource writes a thin normaliser on top of these
// instead of re-deriving fetch policy. Nothing about a wire *type* is hand-written anywhere:
// those come from `components['schemas'][...]` in the generated schema.d.ts.

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
    throw new Error(`${method} ${path} failed: ${res.status}`)
  }
  if (res.status === 204) {
    return null
  }
  return (await res.json()) as T
}

// Raw wire shapes, straight from the generated OpenAPI schema.
type TaskResponse = components['schemas']['TaskResponse']
type CreateTaskRequest = components['schemas']['CreateTaskRequest']

// App-facing Task: `duration` normalised to `number | null`. The generated
// `TaskResponse.duration` is typed `number | string | null` — an artifact of how .NET 10's
// OpenAPI generator describes an int32 (it permits a string on the wire, though the server
// always sends a JSON number). Coercing here at the network boundary keeps that artifact out
// of component code entirely.
export interface Task {
  id: string
  title: string
  duration: number | null
  createdAt: string
}

export type NewTask = Pick<CreateTaskRequest, 'title'> & { duration: number }

function toTask(raw: TaskResponse): Task {
  return {
    id: raw.id,
    title: raw.title,
    duration: raw.duration === null || raw.duration === undefined ? null : Number(raw.duration),
    createdAt: raw.createdAt,
  }
}

export async function fetchTasks(): Promise<Task[]> {
  const raw = await getJson<TaskResponse[]>('/api/tasks')
  return raw === null ? [] : raw.map(toTask)
}

export async function createTask(task: NewTask): Promise<void> {
  await sendJson<TaskResponse>('POST', '/api/tasks', task)
}
