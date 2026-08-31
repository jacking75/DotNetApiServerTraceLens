import type {
  AnalyzeSummary,
  AskEvent,
  AskRequest,
  BrowseResponse,
  EditorResponse,
  EntryPoint,
  FreshnessResponse,
  GraphClass,
  GraphClassSummary,
  GraphDocument,
  GraphNamespaceSummary,
  GraphNode,
  SourceResponse,
} from '../types/graph'

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null
    throw new Error(body?.error ?? `요청에 실패했다 (${response.status}).`)
  }

  return response.json() as Promise<T>
}

/** 분석 대상 경로(.sln 또는 .csproj)로 분석을 실행한다. */
export function analyzeTarget(targetPath: string): Promise<AnalyzeSummary> {
  return request('/api/analyze', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ solutionPath: targetPath }),
  })
}

/**
 * 다시 분석하지 않고, 이전에 저장한 `.tracelens/graph.json`을 그대로 불러온다. 저장된 파일이 없으면 실패한다.
 * 서버 재시작 직후 마지막 분석 결과를 조용히 되살리는 내부 동작에만 쓴다(사용자가 누르는 버튼은 없다).
 */
export function loadSavedGraph(targetPath: string): Promise<AnalyzeSummary> {
  return request('/api/analyze/load', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ solutionPath: targetPath }),
  })
}

/** 사용자가 "불러오기"로 고른 파일(분석 결과를 저장해 둔 JSON)의 내용을 그대로 서버에 전달해 불러온다. */
export function importGraph(rawJson: string): Promise<AnalyzeSummary> {
  return request('/api/analyze/import', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: rawJson,
  })
}

/** 분석 대상을 고르기 위해 로컬 디렉터리 목록을 읽는다. path를 비우면 드라이브 목록을 돌려준다. */
export function browsePath(path?: string): Promise<BrowseResponse> {
  const query = path ? `?${new URLSearchParams({ path }).toString()}` : ''
  return request(`/api/browse${query}`)
}

export function getEntryPoints(): Promise<EntryPoint[]> {
  return request('/api/graph/entrypoints')
}

export function getClasses(): Promise<GraphClassSummary[]> {
  return request('/api/graph/classes')
}

export function getNamespaces(): Promise<GraphNamespaceSummary[]> {
  return request('/api/graph/namespaces')
}

export async function getGraph(): Promise<GraphDocument> {
  return normalizeGraph(await request<GraphDocument>('/api/graph'))
}

/**
 * 옛 스키마로 저장된 그래프를 읽었을 때 이후 버전에서 추가된 필드를 기본값으로 채운다.
 * 화면 코드가 `members`/`interfaceIds`/`startupFlow`의 존재를 매번 확인하지 않아도 되게 하는 것이 목적이다.
 */
export function normalizeGraph(graph: GraphDocument): GraphDocument {
  const withStartupFlow: GraphDocument = { ...graph, startupFlow: graph.startupFlow ?? null }
  if (withStartupFlow.schemaVersion >= 4) return withStartupFlow
  return {
    ...withStartupFlow,
    classes: (withStartupFlow.classes ?? []).map((item): GraphClass => ({
      ...item,
      typeKind: item.typeKind ?? 'Class',
      isAbstract: item.isAbstract ?? false,
      isStatic: item.isStatic ?? false,
      baseTypeId: item.baseTypeId ?? null,
      baseTypeName: item.baseTypeName ?? null,
      interfaceIds: item.interfaceIds ?? [],
      interfaceNames: item.interfaceNames ?? [],
      members: item.members ?? [],
    })),
    nodes: (withStartupFlow.nodes ?? []).map((item): GraphNode => ({
      ...item,
      accessibility: item.accessibility ?? 'Public',
      returnType: item.returnType ?? '',
      parameterTypes: item.parameterTypes ?? [],
    })),
  }
}

export function getFreshness(): Promise<FreshnessResponse> {
  return request('/api/freshness')
}

export function getSource(file: string, start: number, end: number): Promise<SourceResponse> {
  const query = new URLSearchParams({ file, start: String(start), end: String(end) })
  return request(`/api/source?${query.toString()}`)
}

export async function askWithMcp(
  payload: AskRequest,
  onEvent: (event: AskEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetch('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    signal,
  })
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null
    throw new Error(body?.error ?? `질문 요청에 실패했다 (${response.status}).`)
  }
  if (!response.body) {
    throw new Error('응답 스트림을 열 수 없다.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  while (true) {
    const { value, done } = await reader.read()
    buffer += decoder.decode(value, { stream: !done }).replaceAll('\r\n', '\n')
    const blocks = buffer.split('\n\n')
    buffer = blocks.pop() ?? ''
    for (const block of blocks) {
      const data = block.split('\n')
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).trimStart())
        .join('\n')
      if (data) onEvent(JSON.parse(data) as AskEvent)
    }
    if (done) break
  }
}

export function openInEditor(nodeId: string): Promise<EditorResponse> {
  return request('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nodeId, question: null, mode: 'editor' } satisfies AskRequest),
  })
}
