export type NodeKind = 'Controller' | 'Service' | 'Repository' | 'Validator' | 'Infra' | 'Db' | 'Method'

export interface GraphNode {
  id: string
  kind: NodeKind
  displayName: string
  namespace: string
  file: string
  startLine: number
  endLine: number
}

export interface GraphEdge {
  from: string
  to: string
}

export interface EntryPoint {
  nodeId: string
  httpMethod: string
  route: string
}

export interface GraphDocument {
  schemaVersion: number
  solutionPath: string
  analyzedAt: string
  entryPoints: EntryPoint[]
  nodes: GraphNode[]
  edges: GraphEdge[]
  fileHashes: Record<string, string>
}

export interface GraphWindow {
  focusNodeId: string
  nodes: GraphNode[]
  edges: GraphEdge[]
  branchNodeIds: Set<string>
}

export interface AnalyzeSummary {
  entryPointCount: number
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
