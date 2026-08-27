// HAND-WRITTEN — delete this file once `npm run gen:api` has something to point at.
//
// The Minimal API's OpenAPI endpoint (/openapi/v1.json) isn't serving yet, so these types
// are hand-maintained here as the *only* place a Task shape is written by hand. When
// `openapi-typescript` starts generating `src/api/schema.d.ts`, replace this file's exports
// with types derived from that schema and delete this file.

export interface Task {
  id: string
  title: string
  duration: number
}

export type NewTask = Pick<Task, 'title' | 'duration'>
