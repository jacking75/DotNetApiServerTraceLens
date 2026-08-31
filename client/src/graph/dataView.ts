import type {
  CompensationKind,
  DataAccess,
  DataAccessDocument,
  Finding,
  FindingSeverity,
  GraphDocument,
  WritePath,
} from '../types/graph'

/** 실패 지점 × 단계 격자의 칸 하나. */
export type FailCellKind = 'stays' | 'undone' | 'harmless' | 'here' | 'notrun'

export interface FailCell {
  kind: FailCellKind
  label: string
}

/**
 * "i번에서 실패하면 무엇이 남는가"를 격자로 편다.
 * i번보다 앞선 단계는 그 단계의 보상 판정에 따라 남거나(빨강) 되돌아가고, 뒤 단계는 아직 실행되지 않았다.
 */
export function buildFailGrid(path: WritePath): FailCell[][] {
  return path.steps.map((_, failureIndex) => path.steps.map((step, index) => {
    if (index > failureIndex) return { kind: 'notrun', label: '미실행' }
    if (index === failureIndex) return { kind: 'here', label: '실패 지점' }
    return cellForCompensation(step.compensation)
  }))
}

function cellForCompensation(compensation: CompensationKind): FailCell {
  switch (compensation) {
    case 'Yes': return { kind: 'undone', label: '되돌림' }
    case 'Transaction': return { kind: 'undone', label: '롤백' }
    case 'NotNeeded': return { kind: 'harmless', label: '무해' }
    case 'Irreversible': return { kind: 'stays', label: '남는다 · 불가' }
    case 'PartialPaths': return { kind: 'stays', label: '경로 따라 남음' }
    default: return { kind: 'stays', label: '남는다' }
  }
}

export const compensationLabels: Record<CompensationKind, string> = {
  Transaction: '트랜잭션이 처리',
  Yes: '되돌림 있음',
  PartialPaths: '일부 경로만',
  No: '되돌림 없음',
  NotNeeded: '불필요',
  Irreversible: '되돌릴 수 없음',
}

export const severityLabels: Record<FindingSeverity, string> = {
  Critical: '치명',
  Warning: '경고',
  Info: '정보',
}

export interface MatrixRow {
  classId: string
  className: string
  cells: Record<string, { operations: string; findingId: string | null }>
}

export interface AccessMatrix {
  targets: { name: string; dataSource: string; storage: string }[]
  rows: MatrixRow[]
}

/** 접근 지도. 클래스 × (테이블·키) 격자를 접근 목록에서 만든다. */
export function buildAccessMatrix(
  data: DataAccessDocument,
  filter: 'all' | 'write' | 'flagged',
): AccessMatrix {
  const findingByTarget = new Map<string, string>()
  for (const finding of data.findings) {
    if (finding.target && !findingByTarget.has(finding.target)) findingByTarget.set(finding.target, finding.id)
  }

  const accesses = data.accesses.filter((access) => {
    if (filter === 'write') return access.operation === 'Write' || access.operation === 'Delete'
    if (filter === 'flagged') return findingByTarget.has(access.target)
    return true
  })

  const targets = new Map<string, { name: string; dataSource: string; storage: string }>()
  const rows = new Map<string, MatrixRow>()
  for (const access of accesses) {
    if (!access.target) continue
    if (!targets.has(access.target)) {
      targets.set(access.target, { name: access.target, dataSource: access.dataSource, storage: access.storage })
    }

    const className = access.classId.split('.').pop() ?? access.classId
    let row = rows.get(access.classId)
    if (!row) {
      row = { classId: access.classId, className, cells: {} }
      rows.set(access.classId, row)
    }

    const existing = row.cells[access.target]
    const letter = access.operation === 'Read' ? 'R' : access.operation === 'Delete' ? 'D' : 'W'
    const operations = existing
      ? [...new Set([...existing.operations.split(''), letter])].sort().join('')
      : letter
    row.cells[access.target] = { operations, findingId: findingByTarget.get(access.target) ?? null }
  }

  return {
    targets: [...targets.values()].sort((left, right) =>
      left.dataSource.localeCompare(right.dataSource) || left.name.localeCompare(right.name)),
    rows: [...rows.values()].sort((left, right) => left.className.localeCompare(right.className)),
  }
}

export interface EntryDataStep {
  nodeId: string
  displayName: string
  file: string
  depth: number
  accesses: DataAccess[]
}

/**
 * 진입점 하나가 호출을 따라가며 건드리는 저장소를 순서대로 세운다.
 * 호출 그래프를 너비 우선으로 훑되, 같은 깊이에서는 파일·줄 순서를 지킨다.
 */
export function buildEntryDataFlow(
  graph: GraphDocument,
  entryNodeId: string,
  maxDepth = 6,
): EntryDataStep[] {
  const data = graph.dataAccess
  if (!data) return []

  const byMethod = new Map<string, DataAccess[]>()
  for (const access of data.accesses) {
    const list = byMethod.get(access.methodNodeId)
    if (list) list.push(access)
    else byMethod.set(access.methodNodeId, [access])
  }

  const outgoing = new Map<string, string[]>()
  for (const edge of graph.edges) {
    const list = outgoing.get(edge.from)
    if (list) list.push(edge.to)
    else outgoing.set(edge.from, [edge.to])
  }

  const steps: EntryDataStep[] = []
  const seen = new Set([entryNodeId])
  let frontier = [entryNodeId]
  for (let depth = 0; depth <= maxDepth && frontier.length > 0; depth += 1) {
    const next: string[] = []
    for (const nodeId of frontier) {
      const accesses = byMethod.get(nodeId)
      if (accesses && accesses.length > 0) {
        steps.push({
          nodeId,
          displayName: accesses[0].methodDisplayName,
          file: accesses[0].file,
          depth,
          accesses: [...accesses].sort((left, right) => left.line - right.line),
        })
      }

      for (const target of outgoing.get(nodeId) ?? []) {
        if (seen.has(target)) continue
        seen.add(target)
        next.push(target)
      }
    }

    frontier = next
  }

  return steps
}

export interface FindingFilter {
  severity: 'all' | FindingSeverity
  onlyCertain: boolean
}

export function filterFindings(findings: Finding[], filter: FindingFilter): Finding[] {
  return findings.filter((finding) => {
    if (filter.onlyCertain && finding.confidence === 'Estimated') return false
    if (filter.severity !== 'all' && finding.severity !== filter.severity) return false
    return true
  })
}

export function countBySeverity(findings: Finding[]): Record<FindingSeverity, number> {
  const counts: Record<FindingSeverity, number> = { Critical: 0, Warning: 0, Info: 0 }
  for (const finding of findings) counts[finding.severity] += 1
  return counts
}

/** 데이터소스 색을 정하는 키. CSS 클래스 이름으로 그대로 쓴다. */
export function dataSourceTone(kind: string): string {
  if (kind === 'shard') return 'shard'
  if (kind === 'cache') return 'redis'
  return 'single'
}
