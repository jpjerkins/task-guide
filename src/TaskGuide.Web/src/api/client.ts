import type { NewTask, Task } from './types'

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
  return (await res.json()) as Task[]
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
