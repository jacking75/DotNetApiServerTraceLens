import { describe, expect, it } from 'vitest'
import { getDrilldownWindow, getInitialWindow } from './windowing'
import type { GraphDocument, GraphNode } from '../types/graph'

const node = (id: string): GraphNode => ({
  id,
  kind: 'Method',
  displayName: id,
  namespace: 'Tests',
  file: `${id}.cs`,
  startLine: 1,
  endLine: 2,
})

const graph: GraphDocument = {
  schemaVersion: 1,
  solutionPath: 'test.sln',
  analyzedAt: '2026-01-01T00:00:00Z',
  entryPoints: [{ nodeId: 'A', httpMethod: 'GET', route: '/test' }],
  nodes: ['A', 'B', 'C', 'D', 'E', 'F'].map(node),
  edges: [
    { from: 'A', to: 'B' },
    { from: 'B', to: 'C' },
    { from: 'C', to: 'D' },
    { from: 'C', to: 'E' },
    { from: 'D', to: 'F' },
  ],
  fileHashes: {},
}

describe('포커스 기반 윈도잉', () => {
  it('Level 1은 첫 분기 노드까지 직선 체인만 반환한다', () => {
    const result = getInitialWindow(graph, 'A')

    expect(result.nodes.map((item) => item.id)).toEqual(['A', 'B', 'C'])
    expect(result.branchNodeIds.has('C')).toBe(true)
  })

  it('드릴다운은 포커스에서 지정한 홉까지 모든 하위 호출을 반환한다', () => {
    const result = getDrilldownWindow(graph, 'C', 2)

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E', 'F'])
    expect(result.edges).toHaveLength(3)
  })
})
