import type { components } from './schema'

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
  const res = await fetch('/api/tasks')
  if (!res.ok) {
    throw new Error(`GET /api/tasks failed: ${res.status}`)
  }
  // The API currently answers every endpoint with 204 No Content while the store is
  // being wired up — treat "nothing to parse" as "no tasks" rather than a parse error.
  if (res.status === 204) {
    return []
  }
  const raw = (await res.json()) as TaskResponse[]
  return raw.map(toTask)
}

export async function createTask(task: NewTask): Promise<void> {
  const res = await fetch('/api/tasks', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(task),
  })
  if (!res.ok) {
    throw new Error(`POST /api/tasks failed: ${res.status}`)
  }
}
