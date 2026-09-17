export type NodeKind = 'Controller' | 'Service' | 'Repository' | 'Validator' | 'Infra' | 'Db' | 'Method' | 'Interface'

/** 선언 종류. UML 박스의 스테레오타입(«interface», «record»)을 정한다. */
export type ClassTypeKind = 'Class' | 'Record' | 'Struct' | 'Interface'

export type MemberKind = 'Field' | 'Property' | 'Constructor' | 'Method'

export type MemberAccessibility = 'Public' | 'Protected' | 'Internal' | 'Private'

/** 실행 본문을 가진 단위의 모양. v7부터 있다. */
export type CallableKind = 'Method' | 'Constructor' | 'PropertyAccessor' | 'LocalFunction' | 'Lambda'

export interface GraphNode {
  id: string
  kind: NodeKind
  displayName: string
  namespace: string
  classId: string
  file: string
  startLine: number
  endLine: number
  accessibility: MemberAccessibility
  /** 표시용 반환 타입. 생성자는 빈 문자열이다. */
  returnType: string
  parameterTypes: string[]
  /** v7부터 있다. v6 이하 문서를 읽으면 'Method'로 채운다. */
  callable: CallableKind
  /** 프로젝트·오버로드까지 구분하는 비교용 키. v7부터 있다. */
  stableKey: string
  signatureFingerprint: string
  bodyFingerprint: string
}

/** 호출 한 지점을 어떤 근거로 이었는지. v7부터 있다. */
export type CallResolution =
  | 'Direct'
  | 'DiRegistration'
  | 'SingleImplementation'
  | 'Ambiguous'
  | 'Unresolved'
  | 'External'

export type CallConfidence = 'Certain' | 'Estimated'

/** 엣지의 근거가 되는 실제 호출 위치. `to`가 빈 문자열이면 확정하지 못한 호출(경계)이다. */
export interface CallSite {
  from: string
  to: string
  declaredTarget: string
  file: string
  line: number
  resolution: CallResolution
  confidence: CallConfidence
  note: string
  /**
   * 람다 본문 안에서 나온 호출인지. 이 호출은 람다를 받는 쪽이 실제로 부를 때만 실행되므로
   * `confidence`가 항상 'Estimated'다. v7 이전 문서를 읽으면 false로 채운다.
   */
  viaLambda: boolean
}

export interface GraphEdge {
  from: string
  to: string
  /**
   * 근거 중 가장 높은 확실도. 람다 본문 안의 호출만 있으면 'Estimated'다.
   * v7 이전 문서를 읽으면 'Certain'으로 채운다.
   */
  confidence: CallConfidence
}

/**
 * 클래스가 선언한 멤버 한 줄. `nodes`가 실행 그래프라면 이쪽은 화면에 그리는 모델이라
 * 인터페이스 메서드처럼 본문이 없어 노드가 아닌 멤버도 들어간다. 둘을 잇는 것이 `nodeId`다.
 */
export interface GraphClassMember {
  memberKind: MemberKind
  name: string
  signature: string | null
  /** 표시용 타입 문자열. 메서드는 반환 타입, 생성자는 빈 문자열이다. */
  type: string
  /** 그 타입에서 뽑아낸 솔루션 안 타입 id. 연관 관계는 이 값으로 만든다. */
  typeIds: string[]
  accessibility: MemberAccessibility
  isStatic: boolean
  startLine: number
  endLine: number
  nodeId: string | null
}

export interface GraphClass {
  id: string
  name: string
  namespace: string
  kind: NodeKind
  typeKind: ClassTypeKind
  isAbstract: boolean
  isStatic: boolean
  /** 상위 타입이 솔루션 안에 있으면 그 클래스 id, 아니면 null. */
  baseTypeId: string | null
  /** 표시용 상위 타입 이름. 솔루션 밖 타입도 이름은 남는다. */
  baseTypeName: string | null
  interfaceIds: string[]
  interfaceNames: string[]
  file: string
  startLine: number
  endLine: number
  methodIds: string[]
  members: GraphClassMember[]
}

/** 클래스 메뉴용 요약 정보(`/api/graph/classes`). */
export interface GraphClassSummary {
  id: string
  name: string
  namespace: string
  kind: NodeKind
  typeKind: ClassTypeKind
  file: string
  startLine: number
  endLine: number
  methodCount: number
  memberCount: number
}

/** 좌측 네임스페이스 패널용 요약(`/api/graph/namespaces`). */
export interface GraphNamespaceSummary {
  namespace: string
  classCount: number
  methodCount: number
  kinds: NodeKind[]
}

export type EndpointSource = 'Controller' | 'MinimalApi'

export type HandlerKind = 'Method' | 'MethodGroup' | 'Lambda' | 'LocalFunction' | 'Unresolved'

export interface EntryPoint {
  /** 핸들러 노드 ID. 여러 엔드포인트가 같은 값을 가질 수 있다. */
  nodeId: string
  httpMethod: string
  route: string
  /** v7부터 있다. 같은 메서드의 여러 라우트를 구분한다. */
  endpointId: string
  normalizedRoute: string
  source: EndpointSource
  handlerKind: HandlerKind
  confidence: CallConfidence
  registrationFile: string
  registrationLine: number
  handlerDisplayName: string
  projectKey: string
  notes: string[]
}

/* ------------------------------------------------------------------ 분석 진단·입력 목록 (스키마 v7) */

export type ManifestInputKind = 'Solution' | 'Project' | 'Config' | 'BuildProps' | 'PackagesProps' | 'GlobalJson'

export interface ManifestInput {
  path: string
  hash: string
  kind: ManifestInputKind
}

export interface AnalysisManifest {
  inputs: ManifestInput[]
  sourceRoots: string[]
  excludedDirectories: string[]
  linkedSources: string[]
  limitations: string[]
}

export interface AnalyzedProject {
  name: string
  path: string
  loaded: boolean
  errorCount: number
  sampleErrors: string[]
}

export interface CallResolutionStats {
  inspected: number
  resolved: number
  direct: number
  diRegistration: number
  singleImplementation: number
  ambiguous: number
  unresolved: number
  external: number
  /** 해석한 지원 대상 호출 지점 / 검사한 지원 대상 호출 지점. 호출 정확도와는 다른 값이다. */
  resolutionRate: number
}

export interface AnalysisDiagnostics {
  totalProjects: number
  loadedProjects: number
  projectsWithCompileErrors: number
  projects: AnalyzedProject[]
  calls: CallResolutionStats
  excludedNamespacePrefixes: string[]
  maxCallDepth: number
  depthLimitReached: boolean
  analysisScope: string
  limitations: string[]
  changedDuringAnalysis: boolean
  changedDuringAnalysisFiles: string[]
  entryPointNotes: string[]
}

/** 시작 흐름 한 단계의 종류. */
export type StartupStepKind = 'Registration' | 'Configuration' | 'ExtensionCall' | 'Middleware'

export type StartupLifetime = 'Scoped' | 'Singleton' | 'Transient'

/** Program.cs(또는 그 확장 메서드) 안의 한 단계. `children`은 ExtensionCall일 때만 채워진다. */
export interface StartupStep {
  kind: StartupStepKind
  label: string
  serviceType: string | null
  serviceTypeId: string | null
  implementationType: string | null
  implementationTypeId: string | null
  lifetime: StartupLifetime | null
  file: string
  line: number
  children: StartupStep[]
}

/** Program.cs를 분석한 시작 흐름. Program.cs를 찾지 못했으면(구식 Startup.cs 등) null이다. */
export interface StartupFlowDocument {
  programFile: string
  serviceSteps: StartupStep[]
  middlewareSteps: StartupStep[]
}

export interface GraphDocument {
  schemaVersion: number
  /** 분석 대상으로 고른 파일 경로(.sln 또는 .csproj). */
  solutionPath: string
  /** 노드/클래스 상대 경로의 기준 디렉터리. */
  rootDirectory: string
  analyzedAt: string
  entryPoints: EntryPoint[]
  classes: GraphClass[]
  nodes: GraphNode[]
  edges: GraphEdge[]
  fileHashes: Record<string, string>
  /** v7부터 있다. 이 분석 실행 하나를 가리킨다. */
  snapshotId: string
  /** v7부터 있다. 분석 입력만으로 만든 지문. */
  inputFingerprint: string
  /** v7부터 있다. 이 분석이 무엇을 읽었는지. */
  manifest: AnalysisManifest | null
  /** v7부터 있다. 얼마나 보고 무엇을 못 봤는지. */
  diagnostics: AnalysisDiagnostics | null
  /** v7부터 있다. 엣지의 근거가 되는 실제 호출 위치. */
  callSites: CallSite[]
  analyzerVersion: string
  rulesVersion: string
  /** v5부터 있다. v4 이하 문서를 읽으면 null이다. */
  startupFlow: StartupFlowDocument | null
  /** v6부터 있다. 설정에서 껐거나 v5 이하 문서를 읽으면 null이다. */
  dataAccess: DataAccessDocument | null
  /** v6부터 있다. Program.cs를 찾지 못했거나 v5 이하 문서를 읽으면 null이다. */
  middleware: MiddlewarePipelineDocument | null
}

/** 그래프 창의 종류. 지금은 호출 흐름 보기만 이 창을 쓴다. */
export type GraphWindowKind = 'callFlow'

export interface GraphWindow {
  kind: GraphWindowKind
  focusNodeId: string
  /** 상한 때문에 이 창에 넣지 못한 노드 수. 0이면 다 보여 준 것이다. */
  hiddenCount?: number
  nodes: GraphNode[]
  edges: GraphEdge[]
  /** 아직 창에 없는 관계가 남아 있어 눌러서 더 펼칠 수 있는 노드. */
  branchNodeIds: Set<string>
}

/** 상단 보기 전환 툴바가 고르는 보기. */
export type ViewMode = 'callflow' | 'classmap' | 'flowmap' | 'startup' | 'data' | 'middleware'

/**
 * 선택 대상. 클래스 다이어그램은 앞 세 종류를, 시작 흐름은 startupStep을,
 * DB·캐시와 미들웨어 보기는 나머지 여섯 종류를 다룬다.
 */
export type Selection =
  | { type: 'class'; key: string }
  | { type: 'method'; key: string }
  | { type: 'namespace'; key: string }
  | { type: 'startupStep'; key: string }
  | { type: 'finding'; key: string }
  | { type: 'writePath'; key: string }
  | { type: 'dataSource'; key: string }
  | { type: 'storageTarget'; key: string }
  | { type: 'rule'; key: string }
  | { type: 'middlewareStep'; key: string }

export interface AnalyzeSummary {
  /** DB·캐시와 미들웨어 점검에서 잡은 항목 수. v6부터 채워진다. */
  findingCount?: number
  snapshotId?: string
  inputFingerprint?: string
  callSiteCount?: number
  resolutionRate?: number
  unresolvedCallSites?: number
  minimalApiCount?: number
  origin?: 'live' | 'offlineSnapshot'
  /** 진입점 0개·컴파일 오류처럼 바로 알려야 하는 안내. */
  notice?: string | null
  entryPointCount: number
  classCount: number
  interfaceCount: number
  nodeCount: number
  edgeCount: number
  memberCount: number
  graphSizeBytes: number
  elapsedMs: number
  analyzedAt: string
  previousAnalyzedAt: string | null
}

export type FreshnessStatus = 'fresh' | 'stale' | 'unknown'

export type FreshnessChangeKind = 'Modified' | 'Deleted' | 'Added' | 'ConfigChanged' | 'Unreadable'

export interface FreshnessChange {
  path: string
  kind: FreshnessChangeKind
}

export interface FreshnessResponse {
  status: FreshnessStatus
  analyzedAt: string
  changedFiles: string[]
  snapshotId: string
  changes: FreshnessChange[]
  reasons: string[]
  origin: 'live' | 'offlineSnapshot'
}

/** 분석 대상 선택 대화상자가 다루는 파일 시스템 항목. */
export type BrowseEntryKind = 'Directory' | 'Solution' | 'Project'

export interface BrowseEntry {
  name: string
  path: string
  kind: BrowseEntryKind
}

export interface BrowseResponse {
  /** 지금 보고 있는 디렉터리. 드라이브 목록일 때는 null이다. */
  path: string | null
  parentPath: string | null
  entries: BrowseEntry[]
}

export interface SourceResponse {
  file: string
  startLine: number
  endLine: number
  content: string
  /** snapshot이면 분석 당시와 같은 내용이고, current면 그 뒤로 바뀐 현재 소스다. */
  sourceVersion: 'snapshot' | 'current'
  truncated: boolean
}

/** 질문 패널이 고르는 실행 방식. 이름은 실제 동작을 그대로 쓴다. */
export type AskMode = 'context' | 'mcp' | 'editor'

export interface AskRequest {
  /** 선택 종류를 함께 보내면 그 종류로 문맥을 만든다. 비우면 nodeId를 노드로 본다. */
  kind?: string
  id?: string
  nodeId: string | null
  question: string | null
  mode: 'mcp' | 'editor'
  intent?: string
  budget?: string
  expectedSnapshotId?: string
  /** 소스 본문을 함께 넘길지. 미리보기의 같은 옵션과 같은 뜻이다. */
  includeSource?: boolean
  /** 문맥에서 뺄 파일의 상대 경로. 미리보기에서 체크를 끈 파일이다. */
  excludeFiles?: string[]
}

export interface AskEvent {
  /** `run`은 실행 ID를 알려 주는 첫 이벤트다. 이 ID로 DELETE /api/ask/{runId}를 부르면 끊긴다. */
  type: 'run' | 'delta' | 'log' | 'error' | 'done'
  text: string | null
}

export interface EditorResponse {
  opened: boolean
  fallbackUri: string
  error: string | null
}

/* ------------------------------------------------------------------ DB · 캐시 (스키마 v6) */

export type StorageKind = 'Database' | 'Cache' | 'External'
export type AccessOperation = 'Read' | 'Write' | 'Delete' | 'Other'
/** 쓰기 한 단계가 실패했을 때 되돌려지는지에 대한 판정. */
export type CompensationKind = 'Transaction' | 'Yes' | 'PartialPaths' | 'No' | 'NotNeeded' | 'Irreversible'
export type FindingSeverity = 'Critical' | 'Warning' | 'Info'
export type FindingConfidence = 'Certain' | 'Estimated'

export interface FindingEvidence {
  /** ok는 확인된 사실, bad는 문제의 근거, 빈 값은 중립이다. */
  tone: string
  head: string
  detail: string
  note: string
}

export interface Finding {
  id: string
  ruleId: string
  severity: FindingSeverity
  confidence: FindingConfidence
  what: string
  why: string
  fix: string
  symbol: string
  file: string
  line: number
  nodeId: string | null
  pathId: string | null
  stepId: string | null
  target: string | null
  evidence: FindingEvidence[]
}

export interface RuleInfo {
  id: string
  group: string
  name: string
  severity: FindingSeverity
  judge: FindingConfidence
  description: string
  shardOnly: boolean
  /** 이번 분석에서 실제로 검사했는지. false면 화면에 "꺼짐"으로 나온다. */
  enabled: boolean
}

export interface DataAccess {
  id: string
  storage: StorageKind
  dataSource: string
  /** 테이블 이름, 프로시저 이름, 또는 정규화한 캐시 키 패턴. */
  target: string
  operation: AccessOperation
  statement: string
  methodNodeId: string
  methodDisplayName: string
  classId: string
  file: string
  line: number
  inTransaction: boolean
  inLoop: boolean
  /** 이 접근을 얼마나 확실하게 짚었는지. v7 이전 문서를 읽으면 'Certain'으로 채운다. */
  confidence: FindingConfidence
  /** 추정일 때 그 사유 한 줄. */
  note: string
  /** 트랜잭션 래퍼 안이면 그 래퍼 이름. */
  transactionWrapper: string
}

/** 트랜잭션 경계를 무엇으로 잡았는지. v7부터 있다. */
export type TransactionScopeKind = 'Explicit' | 'Wrapper'

export interface TransactionScopeInfo {
  id: string
  methodNodeId: string
  methodDisplayName: string
  classId: string
  file: string
  startLine: number
  hasCommit: boolean
  dataSources: string[]
  accessIds: string[]
  externalCalls: string[]
  cacheCalls: string[]
  /** v7부터 있다. 없으면 'Explicit'으로 채운다. */
  kind: TransactionScopeKind
  /** `kind`가 'Wrapper'일 때 그 래퍼 메서드 이름. */
  wrapperName: string
}

export interface WriteStep {
  order: number
  label: string
  detail: string
  storage: StorageKind
  dataSource: string
  target: string
  operation: AccessOperation
  compensation: CompensationKind
  compensationNote: string
  file: string
  line: number
}

export interface WritePath {
  id: string
  methodNodeId: string
  methodDisplayName: string
  classId: string
  file: string
  startLine: number
  inTransaction: boolean
  note: string
  steps: WriteStep[]
  findingIds: string[]
}

export interface DataSourceInfo {
  name: string
  /** single · shard · cache. */
  kind: string
  shardCount: number
  readOnly: boolean
  writeOnly: boolean
  description: string
  accessCount: number
}

export interface DataAccessDocument {
  shardingEnabled: boolean
  dataSources: DataSourceInfo[]
  accesses: DataAccess[]
  transactions: TransactionScopeInfo[]
  writePaths: WritePath[]
  findings: Finding[]
  rules: RuleInfo[]
}

/* ------------------------------------------------------------------ 미들웨어 (스키마 v6) */

export type MiddlewareType = 'Custom' | 'Framework'
export type ContextAccessMode = 'Read' | 'Write'

/**
 * 파이프라인 한 줄의 성격. v7부터 있다. 없으면 'Middleware'로 채운다.
 * `MapControllers()`·`MapGet()`은 엔드포인트 **등록**이지 미들웨어가 아니다 — 실행은 파이프라인 맨 끝이다.
 */
export type MiddlewareStepKind = 'Middleware' | 'EndpointRegistration' | 'Terminal'

export interface MiddlewareShortCircuit {
  statusCode: string
  condition: string
  line: number
}

export interface ContextAccess {
  mode: ContextAccessMode
  target: string
}

export interface EndpointFilterInfo {
  name: string
  detail: string
  statusCode: string | null
}

export interface MiddlewareStepInfo {
  id: string
  order: number
  name: string
  type: MiddlewareType
  registrationFile: string
  registrationLine: number
  classId: string | null
  implementationFile: string | null
  implementationLine: number
  /** next 호출 전에 하는 일. */
  pre: string | null
  /** next 이후(응답이 돌아올 때) 하는 일. 없으면 null. */
  post: string | null
  shortCircuit: MiddlewareShortCircuit | null
  contextAccesses: ContextAccess[]
  storeAccesses: string[]
  filters: EndpointFilterInfo[]
  isTerminal: boolean
  isDead: boolean
  condition: string | null
  note: string | null
  /** v7부터 있다. 없으면 'Middleware'로 채운다. */
  kind: MiddlewareStepKind
}

export interface MiddlewarePipelineDocument {
  programFile: string
  steps: MiddlewareStepInfo[]
  findings: Finding[]
  rules: RuleInfo[]
}
