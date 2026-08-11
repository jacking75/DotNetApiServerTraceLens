import type { AnalyzeSummary, EntryPoint, FreshnessResponse, GraphDocument, SourceResponse } from '../types/graph'

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null
    throw new Error(body?.error ?? `요청에 실패했다 (${response.status}).`)
  }

  return response.json() as Promise<T>
}

export function analyzeSolution(solutionPath: string): Promise<AnalyzeSummary> {
  return request('/api/analyze', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ solutionPath }),
  })
}

export function getEntryPoints(): Promise<EntryPoint[]> {
  return request('/api/graph/entrypoints')
}

export function getGraph(): Promise<GraphDocument> {
  return request('/api/graph')
}

export function getFreshness(): Promise<FreshnessResponse> {
  return request('/api/freshness')
}

export function getSource(file: string, start: number, end: number): Promise<SourceResponse> {
  const query = new URLSearchParams({ file, start: String(start), end: String(end) })
  return request(`/api/source?${query.toString()}`)
}
