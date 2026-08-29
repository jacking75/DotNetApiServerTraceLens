export type NodeKind = 'Controller' | 'Service' | 'Repository' | 'Validator' | 'Infra' | 'Db' | 'Method' | 'Interface'

/** 선언 종류. UML 박스의 스테레오타입(«interface», «record»)을 정한다. */
export type ClassTypeKind = 'Class' | 'Record' | 'Struct' | 'Interface'

export type MemberKind = 'Field' | 'Property' | 'Constructor' | 'Method'

export type MemberAccessibility = 'Public' | 'Protected' | 'Internal' | 'Private'

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
}

export interface GraphEdge {
  from: string
  to: string
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

export interface EntryPoint {
  nodeId: string
  httpMethod: string
  route: string
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
}

/** 그래프 창의 종류. 지금은 호출 흐름 보기만 이 창을 쓴다. */
export type GraphWindowKind = 'callFlow'

export interface GraphWindow {
  kind: GraphWindowKind
  focusNodeId: string
  nodes: GraphNode[]
  edges: GraphEdge[]
  /** 아직 창에 없는 관계가 남아 있어 눌러서 더 펼칠 수 있는 노드. */
  branchNodeIds: Set<string>
}

/** 상단 보기 전환 툴바가 고르는 세 가지 보기. */
export type ViewMode = 'callflow' | 'classmap' | 'flowmap'

/** 선택 대상. 클래스 다이어그램은 세 종류를 모두 다룬다. */
export type Selection =
  | { type: 'class'; key: string }
  | { type: 'method'; key: string }
  | { type: 'namespace'; key: string }

export interface AnalyzeSummary {
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

export interface FreshnessResponse {
  status: 'fresh' | 'stale'
  analyzedAt: string
  changedFiles: string[]
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
}

export type AskMode = 'clipboard' | 'mcp' | 'editor'

export interface AskRequest {
  nodeId: string
  question: string | null
  mode: 'mcp' | 'editor'
}

export interface AskEvent {
  type: 'delta' | 'log' | 'error' | 'done'
  text: string | null
}

export interface EditorResponse {
  opened: boolean
  fallbackUri: string
  error: string | null
}
