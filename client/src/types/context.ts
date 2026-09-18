/**
 * `POST /api/context`·`/api/impact`·`/api/snapshots`·`/api/diagnostics`의 응답 계약.
 * 서버(TraceLens.Context)와 MCP가 같은 DTO를 돌려주므로 화면과 AI가 같은 근거를 본다.
 */

export type SelectionKind =
  | 'Node'
  | 'Endpoint'
  | 'Class'
  | 'Namespace'
  | 'Finding'
  | 'WritePath'
  | 'MiddlewareStep'
  | 'StartupStep'

export type ContextIntent = 'Explain' | 'PlanChange' | 'ReviewChange' | 'InvestigateData' | 'Share'

export type ContextBudget = 'Short' | 'Balanced' | 'Detailed'

export interface ContextSelection {
  kind: SelectionKind
  id: string
  displayName: string
}

export interface ContextFreshness {
  status: string
  reasons: string[]
  changedFiles: string[]
}

export interface ContextEvidence {
  id: string
  kind: string
  file: string
  startLine: number
  endLine: number
  resolution: string
  confidence: string
  detail: string
  /** 람다 본문 안의 호출이라 실행 여부를 이 자리에서 증명할 수 없는 근거인지. */
  viaLambda: boolean
}

export interface ContextSourceSnippet {
  evidenceId: string
  file: string
  startLine: number
  endLine: number
  content: string
  truncated: boolean
  sourceVersion: string
  omittedRanges: string[]
}

export interface ContextPathStep {
  nodeId: string
  displayName: string
  file: string
  startLine: number
  resolution: string
  confidence: string
  /** 이 단계로 오는 호출이 람다 본문 안에 있는지. */
  viaLambda: boolean
}

export interface ContextPath {
  endpointId: string
  httpMethod: string
  route: string
  steps: ContextPathStep[]
  truncated: boolean
}

export interface ContextNeighbor {
  nodeId: string
  displayName: string
  file: string
  startLine: number
  kind: string
}

export interface ContextFindingView {
  id: string
  ruleId: string
  severity: string
  confidence: string
  what: string
  why: string
  fix: string
  symbol: string
  file: string
  line: number
}

export interface ContextAnalysis {
  analyzedAt: string
  analysisScope: string
  loadedProjects: number
  totalProjects: number
  projectsWithCompileErrors: number
  inspectedCallSites: number
  resolvedCallSites: number
  resolutionRate: number
}

export interface ContextOmission {
  nodes: number
  sourceRanges: string[]
  sections: string[]
}

export interface ContextDocument {
  contextVersion: number
  snapshotId: string
  inputFingerprint: string
  selection: ContextSelection
  intent: string
  budget: string
  freshness: ContextFreshness
  summary: string
  analysis: ContextAnalysis
  evidence: ContextEvidence[]
  sourceSnippets: ContextSourceSnippet[]
  paths: ContextPath[]
  callers: ContextNeighbor[]
  callees: ContextNeighbor[]
  findings: ContextFindingView[]
  testCandidates: ContextNeighbor[]
  limitations: string[]
  truncated: boolean
  omitted: ContextOmission
  question: string
  markdown: string
  characters: number
  /** 모델별 tokenizer가 아니라 문자 수로 추정한 값이다. */
  estimatedTokens: number
  containsSource: boolean
  files: string[]
}

export interface ContextRequestBody {
  kind: SelectionKind
  id: string
  intent?: ContextIntent
  budget?: ContextBudget
  question?: string
  expectedSnapshotId?: string
  includeSource?: boolean
  excludeFiles?: string[]
}

/* ------------------------------------------------------------------ 변경 영향 */

export interface ImpactPathStep {
  nodeId: string
  displayName: string
  file: string
  startLine: number
  resolution: string
  confidence: string
}

export interface ImpactEndpoint {
  endpointId: string
  httpMethod: string
  route: string
  handlerNodeId: string
  handlerDisplayName: string
  distance: number
  confidence: string
  path: ImpactPathStep[]
}

export interface ImpactTestCandidate {
  nodeId: string
  displayName: string
  file: string
  startLine: number
  /** reached면 그래프에서 도달했다는 뜻이고, nameMatch면 이름만 비슷하다는 뜻이다. */
  evidence: string
}

export interface ImpactResult {
  snapshotId: string
  targets: string[]
  endpoints: ImpactEndpoint[]
  unreachableTargets: string[]
  testCandidates: ImpactTestCandidate[]
  truncated: boolean
  truncationReasons: string[]
  limitations: string[]
  visitedNodes: number
}

/* ------------------------------------------------------------------ 스냅샷 비교 */

export type DiffKind = 'Added' | 'Removed' | 'Changed' | 'Unchanged' | 'NotComparable'

export interface DiffEntry {
  id: string
  label: string
  kind: DiffKind
  detail: string
  file: string
  line: number
}

export interface ComparePreconditions {
  ok: boolean
  differences: string[]
  notes: string[]
}

export interface DiffCounts {
  endpointsAdded: number
  endpointsRemoved: number
  methodsAdded: number
  methodsRemoved: number
  methodsChanged: number
  edgesAdded: number
  edgesRemoved: number
  findingsAdded: number
  findingsResolved: number
}

export interface SnapshotDiff {
  baselineId: string
  currentId: string
  baselineAnalyzedAt: string
  currentAnalyzedAt: string
  preconditions: ComparePreconditions
  endpoints: DiffEntry[]
  methods: DiffEntry[]
  edges: DiffEntry[]
  dataAccess: DiffEntry[]
  findings: DiffEntry[]
  renameCandidates: DiffEntry[]
  limitations: string[]
  counts: DiffCounts
}

export interface SnapshotEntry {
  snapshotId: string
  inputFingerprint: string
  analyzedAt: string
  savedAt: string
  label: string
  solutionPath: string
  analyzerVersion: string
  rulesVersion: string
  analysisScope: string
  nodeCount: number
  edgeCount: number
  entryPointCount: number
  findingCount: number
}

export interface SnapshotListResponse {
  currentSnapshotId: string
  currentAnalyzedAt: string
  entries: SnapshotEntry[]
  softLimit: number
}

export interface SaveSnapshotResponse {
  entry: SnapshotEntry
  totalCount: number
  notice: string | null
  /** 상한에 닿았을 때 "지워도 될 후보"(가장 오래된 것부터). 서버는 자동으로 지우지 않는다. */
  pruneCandidates: string[]
}

/* ------------------------------------------------------------------ 환경 점검 · 분석 상태 */

export type DiagnosticSeverity = 'Ok' | 'Warning' | 'Error'

export interface DiagnosticItem {
  code: string
  severity: DiagnosticSeverity
  message: string
  suggestedAction: string
  /** 경로·포트처럼 번역하지 않는 값. UI는 코드로 문장을 번역하고 이 값을 덧붙인다. */
  detail: string
}

/** 이번 실행의 로그 위치와 최근 오류. 비밀값·환경 변수는 담지 않는다. */
export interface DiagnosticsLogInfo {
  logFilePath: string | null
  recentErrors: string[]
}

export interface DiagnosticsReport {
  canAnalyze: boolean
  toolVersion: string
  runtimeVersion: string
  operatingSystem: string
  sdkPath: string | null
  supportedTargets: string[]
  unsupportedTargets: Record<string, string>
  items: DiagnosticItem[]
  log?: DiagnosticsLogInfo | null
}

export interface AnalysisCapabilities {
  minimalApi: boolean
  controllers: boolean
  callSites: boolean
  manifest: boolean
  dataAccess: boolean
  middleware: boolean
  startupFlow: boolean
}

export interface AnalysisStatus {
  snapshotId: string
  inputFingerprint: string
  solutionPath: string
  rootDirectory: string
  analyzedAt: string
  schemaVersion: number
  analyzerVersion: string
  rulesVersion: string
  origin: 'live' | 'offlineSnapshot'
  freshness: string
  freshnessReasons: string[]
  changedFiles: string[]
  capabilities: AnalysisCapabilities
}

/* ------------------------------------------------------------------ MCP 설정 미리보기 · 공유 보고서 */

export interface McpHostSnippet {
  host: string
  file: string
  format: string
  content: string
  note: string
}

/** 복사해서 그대로 실행할 수 있는 한 줄. 경로는 항상 따옴표로 감싼다. */
export interface McpOneLiner {
  host: string
  command: string
}

export interface McpSetupPreview {
  graphPath: string
  command: string
  args: string[]
  hosts: McpHostSnippet[]
  warning: string | null
  /** 호스트별 등록 원라이너. */
  oneLiners: McpOneLiner[]
  /** 연결을 확인하는 질문. */
  verifyPrompt: string
  /** 대상 저장소의 CLAUDE.md·AGENTS.md에 붙이는 안내문. */
  agentGuide: string
}

export interface ShareReport {
  title: string
  markdown: string
  included: string[]
}

/* ------------------------------------------------------------------ 분석 작업(진행 상태) */

export interface AnalysisJob {
  id: string
  targetPath: string
  startedAt: string
  state: 'Running' | 'Succeeded' | 'Failed' | 'Canceled'
  error: string | null
  finishedAt: string | null
}

export interface AnalysisJobEvent {
  kind: 'progress' | 'done' | 'error' | 'canceled'
  stage: string
  message: string
  count: number | null
  result: unknown
  /** 실패 이벤트의 분류 코드. UI는 이 값으로 한 줄 요약과 다음 행동을 고른다. */
  code?: string | null
}

/** 배포 패키지에 함께 들어가는 예제 하나. `available`이 false면 소스 폴더를 찾지 못한 것이다. */
export interface SampleView {
  id: string
  title: string
  description: string
  relativeTargetPath: string
  expected: string[]
  learningSteps: string[]
  available: boolean
}

/** `copied`가 false면 이미 복사돼 있던 것을 그대로 쓴다. */
export interface PrepareSampleResponse {
  id: string
  targetPath: string
  copied: boolean
}
