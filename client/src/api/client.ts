import type {
  AnalysisJob,
  AnalysisJobEvent,
  ContextDocument,
  ContextRequestBody,
  DiagnosticsReport,
  ImpactResult,
  McpSetupPreview,
  PrepareSampleResponse,
  SampleView,
  SaveSnapshotResponse,
  ShareReport,
  SnapshotDiff,
  SnapshotListResponse,
} from '../types/context'
import type {
  AnalyzeSummary,
  AskEvent,
  AskRequest,
  BrowseResponse,
  EditorResponse,
  EntryPoint,
  FreshnessResponse,
  GraphClass,
  GraphDocument,
  GraphNamespaceSummary,
  GraphNode,
  SourceResponse,
} from '../types/graph'

/** 서버가 `{ code, message, details }`로 돌려주는 오류. UI는 code로 번역한다. */
export class ApiError extends Error {
  constructor(message: string, readonly code: string, readonly status: number, readonly details?: unknown) {
    super(message)
    this.name = 'ApiError'
  }
}

async function readError(response: Response): Promise<ApiError> {
  const body = (await response.json().catch(() => null)) as
    | { error?: string; code?: string; message?: string; details?: unknown }
    | null
  return new ApiError(
    body?.message ?? body?.error ?? `요청에 실패했다 (${response.status}).`,
    body?.code ?? 'unknown',
    response.status,
    body?.details,
  )
}

/** 네트워크 자체가 끊긴 경우도 ApiError로 통일한다. 화면은 code로 번역하므로 브라우저 원문이 새지 않는다. */
export async function fetchOrThrow(url: string, init?: RequestInit): Promise<Response> {
  try {
    return await fetch(url, init)
  } catch (reason) {
    if (reason instanceof DOMException && reason.name === 'AbortError') throw reason
    throw new ApiError(reason instanceof Error ? reason.message : String(reason), 'NETWORK', 0)
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetchOrThrow(url, init)
  if (!response.ok) throw await readError(response)
  return response.json() as Promise<T>
}

function postJson<T>(url: string, body: unknown): Promise<T> {
  return request(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

/** 분석 대상 경로(.sln 또는 .csproj)로 분석을 실행한다. */
export function analyzeTarget(targetPath: string): Promise<AnalyzeSummary> {
  return postJson('/api/analyze', { solutionPath: targetPath })
}

/**
 * 다시 분석하지 않고, 이전에 저장한 `.tracelens/graph.json`을 그대로 불러온다. 저장된 파일이 없으면 실패한다.
 * 서버 재시작 직후 마지막 분석 결과를 조용히 되살리는 내부 동작에만 쓴다(사용자가 누르는 버튼은 없다).
 */
export function loadSavedGraph(targetPath: string): Promise<AnalyzeSummary> {
  return postJson('/api/analyze/load', { solutionPath: targetPath })
}

/** 사용자가 "불러오기"로 고른 파일(분석 결과를 저장해 둔 JSON)의 내용을 그대로 서버에 전달해 불러온다. */
export function importGraph(rawJson: string): Promise<AnalyzeSummary> {
  return request('/api/analyze/import', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: rawJson,
  })
}

/** 가져온 분석 결과의 원본 루트를 연결한다. 입력이 일치할 때만 소스 접근이 열린다. */
export function adoptImportedGraph(rootDirectory: string): Promise<{ origin: string; rootDirectory: string }> {
  return postJson('/api/analyze/adopt', { rootDirectory })
}

/** 분석 대상을 고르기 위해 로컬 디렉터리 목록을 읽는다. path를 비우면 드라이브 목록을 돌려준다. */
export function browsePath(path?: string): Promise<BrowseResponse> {
  const query = path ? `?${new URLSearchParams({ path }).toString()}` : ''
  return request(`/api/browse${query}`)
}

export function getEntryPoints(): Promise<EntryPoint[]> {
  return request('/api/graph/entrypoints')
}

export function getNamespaces(): Promise<GraphNamespaceSummary[]> {
  return request('/api/graph/namespaces')
}

export async function getGraph(): Promise<GraphDocument> {
  return normalizeGraph(await request<GraphDocument>('/api/graph'))
}

/**
 * 옛 스키마로 저장된 그래프를 읽었을 때 이후 버전에서 추가된 필드를 기본값으로 채운다.
 * 화면 코드가 `members`/`callSites`/`diagnostics`의 존재를 매번 확인하지 않아도 되게 하는 것이 목적이다.
 * 없던 정보를 추정으로 채우지는 않는다(모르는 것은 빈 값·null로 남긴다).
 */
export function normalizeGraph(graph: GraphDocument): GraphDocument {
  const base: GraphDocument = {
    ...graph,
    startupFlow: graph.startupFlow ?? null,
    middleware: graph.middleware
      ? {
          ...graph.middleware,
          steps: (graph.middleware.steps ?? []).map((item) => ({ ...item, kind: item.kind ?? 'Middleware' })),
        }
      : graph.middleware,
    manifest: graph.manifest ?? null,
    diagnostics: graph.diagnostics ?? null,
    callSites: (graph.callSites ?? []).map((item) => ({ ...item, viaLambda: item.viaLambda ?? false })),
    edges: (graph.edges ?? []).map((item) => ({ ...item, confidence: item.confidence ?? 'Certain' })),
    dataAccess: graph.dataAccess
      ? {
          ...graph.dataAccess,
          accesses: (graph.dataAccess.accesses ?? []).map((item) => ({
            ...item,
            confidence: item.confidence ?? 'Certain',
            note: item.note ?? '',
            transactionWrapper: item.transactionWrapper ?? '',
          })),
          transactions: (graph.dataAccess.transactions ?? []).map((item) => ({
            ...item,
            kind: item.kind ?? 'Explicit',
            wrapperName: item.wrapperName ?? '',
          })),
        }
      : graph.dataAccess,
    snapshotId: graph.snapshotId ?? '',
    inputFingerprint: graph.inputFingerprint ?? '',
    analyzerVersion: graph.analyzerVersion ?? '',
    rulesVersion: graph.rulesVersion ?? '',
    entryPoints: (graph.entryPoints ?? []).map((item) => ({
      ...item,
      // v6 이하에는 endpointId가 없다. 라우트·동사·nodeId로 호환 생성하되 유실된 복수 라우트를 복원하지는 않는다.
      endpointId: item.endpointId || `${item.httpMethod} ${item.route} :: ${item.nodeId}`,
      normalizedRoute: item.normalizedRoute ?? item.route,
      source: item.source ?? 'Controller',
      handlerKind: item.handlerKind ?? 'Method',
      confidence: item.confidence ?? 'Certain',
      registrationFile: item.registrationFile ?? '',
      registrationLine: item.registrationLine ?? 0,
      handlerDisplayName: item.handlerDisplayName ?? '',
      projectKey: item.projectKey ?? '',
      notes: item.notes ?? [],
    })),
    nodes: (graph.nodes ?? []).map((item): GraphNode => ({
      ...item,
      accessibility: item.accessibility ?? 'Public',
      returnType: item.returnType ?? '',
      parameterTypes: item.parameterTypes ?? [],
      callable: item.callable ?? 'Method',
      stableKey: item.stableKey ?? '',
      signatureFingerprint: item.signatureFingerprint ?? '',
      bodyFingerprint: item.bodyFingerprint ?? '',
    })),
  }
  if (base.schemaVersion >= 4) return base
  return {
    ...base,
    classes: (base.classes ?? []).map((item): GraphClass => ({
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
  }
}

/** 배포 패키지에 들어 있는 예제 목록. 소스 빌드에서 samples 폴더가 없으면 빈 목록이다. */
export function getSamples(): Promise<SampleView[]> {
  return request('/api/samples')
}

/** 예제를 사용자 영역으로 복사하고 분석할 대상 경로를 받는다. 이미 있으면 다시 복사하지 않는다. */
export function prepareSample(id: string, overwrite = false): Promise<PrepareSampleResponse> {
  return postJson(`/api/samples/${encodeURIComponent(id)}/prepare`, { overwrite })
}

export function getFreshness(): Promise<FreshnessResponse> {
  return request('/api/freshness')
}

export function getDiagnostics(target?: string): Promise<DiagnosticsReport> {
  const query = target ? `?${new URLSearchParams({ target }).toString()}` : ''
  return request(`/api/diagnostics${query}`)
}

export function getSource(file: string, start: number, end: number): Promise<SourceResponse> {
  const query = new URLSearchParams({ file, start: String(start), end: String(end) })
  return request(`/api/source?${query.toString()}`)
}

/* ------------------------------------------------------------------ 문맥 · 영향 · 비교 */

/** 선택과 목적에서 AI에 전달할 문맥을 만든다. UI 미리보기·클립보드·인앱 실행이 모두 이 응답을 쓴다. */
export function buildContext(body: ContextRequestBody): Promise<ContextDocument> {
  return postJson('/api/context', body)
}

/** 선택 범위만 담는 공유용 Markdown 보고서. 절대 경로와 소스 본문은 들어가지 않는다. */
export function buildReport(body: {
  kind: ContextRequestBody['kind']
  id: string
  includePaths?: boolean
  includeStorageNames?: boolean
  title?: string
}): Promise<ShareReport> {
  return postJson('/api/report', body)
}

export function getImpact(nodeIds: string[], expectedSnapshotId?: string): Promise<ImpactResult> {
  return postJson('/api/impact', { nodeIds, expectedSnapshotId })
}

export function listSnapshots(): Promise<SnapshotListResponse> {
  return request('/api/snapshots')
}

export function saveSnapshot(label: string): Promise<SaveSnapshotResponse> {
  return postJson('/api/snapshots', { label })
}

export function compareSnapshots(baselineId: string, currentId?: string): Promise<SnapshotDiff> {
  return postJson('/api/snapshots/compare', { baselineId, currentId })
}

export async function deleteSnapshot(snapshotId: string): Promise<void> {
  const response = await fetch(`/api/snapshots/${encodeURIComponent(snapshotId)}`, { method: 'DELETE' })
  if (!response.ok) throw await readError(response)
}

/* ------------------------------------------------------------------ 분석 작업(진행 상태·취소) */

export function startAnalysisJob(targetPath: string): Promise<AnalysisJob> {
  return postJson('/api/analysis-jobs', { solutionPath: targetPath })
}

export async function cancelAnalysisJob(jobId: string): Promise<void> {
  await fetch(`/api/analysis-jobs/${encodeURIComponent(jobId)}`, { method: 'DELETE' })
}

/** 분석 작업의 단계별 진행 상태를 받는다. 근거 없는 진행률(%) 대신 단계와 처리 건수가 온다. */
export async function watchAnalysisJob(
  jobId: string,
  onEvent: (event: AnalysisJobEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetchOrThrow(`/api/analysis-jobs/${encodeURIComponent(jobId)}/events`, { signal })
  if (!response.ok) throw await readError(response)
  await readServerSentEvents(response, onEvent)
}

/* ------------------------------------------------------------------ AI 연동 */

export function getMcpSetup(): Promise<McpSetupPreview> {
  return request('/api/mcp-setup')
}

export async function askWithMcp(
  payload: AskRequest,
  onEvent: (event: AskEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetchOrThrow('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    signal,
  })
  if (!response.ok) throw await readError(response)
  await readServerSentEvents(response, onEvent)
}

/** 실행 중인 에이전트를 서버에서 끊는다. SSE 연결만 닫으면 프로세스가 남을 수 있다. */
export function cancelAskRun(runId: string): Promise<{ canceled: string }> {
  return request(`/api/ask/${encodeURIComponent(runId)}`, { method: 'DELETE' })
}

export function openInEditor(nodeId: string): Promise<EditorResponse> {
  return postJson('/api/ask', { nodeId, question: null, mode: 'editor' } satisfies AskRequest)
}

/**
 * SSE 버퍼에서 완결된 블록을 꺼낸다. 아직 끝나지 않은 마지막 조각은 `rest`로 남긴다.
 * 순수 함수라 단위 테스트가 가능하다.
 */
export function parseServerSentEvents(buffer: string): { data: string[]; rest: string } {
  const blocks = buffer.replaceAll('\r\n', '\n').split('\n\n')
  const rest = blocks.pop() ?? ''
  const data = blocks
    .map((block) => block.split('\n')
      .filter((line) => line.startsWith('data:'))
      .map((line) => line.slice(5).trimStart())
      .join('\n'))
    .filter((item) => item.length > 0)
  return { data, rest }
}

/** SSE 응답을 한 줄씩 읽어 이벤트로 넘긴다. `/api/ask`와 분석 작업이 같은 형식을 쓴다. */
async function readServerSentEvents<T>(response: Response, onEvent: (event: T) => void): Promise<void> {
  if (!response.body) throw new ApiError('응답 스트림을 열 수 없다.', 'NETWORK', 0)

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  while (true) {
    const { value, done } = await reader.read()
    buffer += decoder.decode(value, { stream: !done })
    const { data, rest } = parseServerSentEvents(buffer)
    buffer = rest
    for (const item of data) {
      try {
        onEvent(JSON.parse(item) as T)
      } catch {
        // 깨진 줄 하나 때문에 스트림 전체를 죽이지 않는다. 그 줄만 버린다.
      }
    }

    if (done) break
  }
}
