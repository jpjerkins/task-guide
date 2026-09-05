import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getJson, sendJson } from './client'

// The shared request policy client.ts is built on: non-OK throws naming method/path/status, a
// 204 reads as "nothing to parse" (the API answers every endpoint 204 while the store is being
// wired up), and JSON parses otherwise. #102 and later tickets add thin per-resource normalisers
// on top of these rather than re-deriving fetch policy.
beforeEach(() => {
  vi.restoreAllMocks()
})

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('getJson', () => {
  it('throws an error naming method, path and status on a non-OK response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))

    await expect(getJson('/api/widgets')).rejects.toThrow(/GET \/api\/widgets failed: 500/)
  })

  it('treats a 204 as absence, not a parse error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(getJson('/api/widgets')).resolves.toBeNull()
  })

  it('parses a 200 body as JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ id: '1', duration: '30' })))

    await expect(getJson('/api/widgets/1')).resolves.toEqual({ id: '1', duration: '30' })
  })
})

describe('sendJson', () => {
  it('throws an error naming method, path and status on a non-OK response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 400 })))

    await expect(sendJson('POST', '/api/widgets', { title: 'x' })).rejects.toThrow(
      /POST \/api\/widgets failed: 400/,
    )
  })

  it('treats a 204 response as absence', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(sendJson('POST', '/api/widgets', { title: 'x' })).resolves.toBeNull()
  })
})
