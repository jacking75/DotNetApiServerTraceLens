export type NodeKind = 'Controller' | 'Service' | 'Repository' | 'Validator' | 'Infra' | 'Db' | 'Method'

export interface GraphNode {
  id: string
  kind: NodeKind
  displayName: string
  namespace: string
  classId: string
  file: string
  startLine: number
  endLine: number
}

export interface GraphEdge {
  from: string
  to: string
}

export interface GraphClass {
  id: string
  name: string
  namespace: string
  kind: NodeKind
  file: string
  startLine: number
  endLine: number
  methodIds: string[]
}

/** 클래스 메뉴용 요약 정보(`/api/graph/classes`). */
export interface GraphClassSummary {
  id: string
  name: string
  namespace: string
  kind: NodeKind
  file: string
  startLine: number
  endLine: number
  methodCount: number
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

export interface GraphWindow {
  focusNodeId: string
  nodes: GraphNode[]
  edges: GraphEdge[]
  branchNodeIds: Set<string>
  /** 클래스 중심 창일 때 하나의 박스로 묶어서 보여줄 클래스. */
  groupClassId: string | null
  groupLabel: string | null
}

export interface AnalyzeSummary {
  entryPointCount: number
  classCount: number
  nodeCount: number
  edgeCount: number
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
