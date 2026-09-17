import { describe, expect, it } from 'vitest'
import { getDrilldownWindow, getInitialWindow } from './windowing'
import type { GraphClass, GraphDocument, GraphNode } from '../types/graph'
import { entryPoint, graphDefaults, nodeDefaults } from '../types/testFactories'

const node = (id: string, classId = 'Tests.Sample'): GraphNode => ({
  id,
  kind: 'Method',
  displayName: id,
  namespace: 'Tests',
  classId,
  file: `${id}.cs`,
  startLine: 1,
  endLine: 2,
  accessibility: 'Public',
  returnType: 'void',
  parameterTypes: [],
  ...nodeDefaults,
})

const graphClass = (id: string, name: string, methodIds: string[]): GraphClass => ({
  id,
  name,
  namespace: 'Tests',
  kind: 'Method',
  typeKind: 'Class',
  isAbstract: false,
  isStatic: false,
  baseTypeId: null,
  baseTypeName: null,
  interfaceIds: [],
  interfaceNames: [],
  file: `${name}.cs`,
  startLine: 1,
  endLine: 9,
  methodIds,
  members: [],
})

const graph: GraphDocument = {
  schemaVersion: 4,
  solutionPath: 'test.sln',
  rootDirectory: '.',
  analyzedAt: '2026-01-01T00:00:00Z',
  entryPoints: [entryPoint('A', 'GET', '/test')],
  classes: [
    graphClass('Tests.Sample', 'Sample', ['A', 'B', 'C', 'D', 'E', 'F']),
  ],
  nodes: ['A', 'B', 'C', 'D', 'E', 'F'].map((id) => node(id)),
  edges: [
    { from: 'A', to: 'B', confidence: 'Certain' },
    { from: 'B', to: 'C', confidence: 'Certain' },
    { from: 'C', to: 'D', confidence: 'Certain' },
    { from: 'C', to: 'E', confidence: 'Certain' },
    { from: 'D', to: 'F', confidence: 'Certain' },
  ],
  fileHashes: {},
  ...graphDefaults,
  startupFlow: null,
  dataAccess: null,
  middleware: null,
}

describe('포커스 기반 윈도잉', () => {
  it('Level 1은 직선 체인과 첫 분기 지점의 직접 호출 대상까지 반환한다', () => {
    const result = getInitialWindow(graph, 'A')

    expect(result.nodes.map((item) => item.id)).toEqual(['A', 'B', 'C', 'D', 'E'])
    // 펼쳐진 분기 노드에는 표시가 남지 않고, 아직 못 펼친 하위 호출이 있는 노드에만 붙는다.
    expect(result.branchNodeIds.has('C')).toBe(false)
    expect(result.branchNodeIds.has('D')).toBe(true)
    expect(result.kind).toBe('callFlow')
  })

  it('진입점이 바로 여러 곳을 호출해도 그 호출 대상을 보여준다', () => {
    const result = getInitialWindow(graph, 'C')

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E'])
    expect(result.edges).toHaveLength(2)
  })

  it('호출 대상이 없는 진입점은 자기 노드만 반환한다', () => {
    const result = getInitialWindow(graph, 'F')

    expect(result.nodes.map((item) => item.id)).toEqual(['F'])
    expect(result.branchNodeIds.size).toBe(0)
  })

  it('드릴다운은 포커스에서 지정한 홉까지 모든 하위 호출을 반환한다', () => {
    const result = getDrilldownWindow(graph, 'C', 2)

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E', 'F'])
    expect(result.edges).toHaveLength(3)
  })
})
