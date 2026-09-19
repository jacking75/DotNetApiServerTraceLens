import { describe, expect, it } from 'vitest'
import { getCallerWindow, getDrilldownWindow, getInitialWindow, getPathWindow, MaxWindowNodes } from './windowing'
import type { GraphClass, GraphDocument, GraphEdge, GraphNode } from '../types/graph'
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

describe('창 크기 상한', () => {
  /** 한 진입점에서 폭넓게 갈라지는 그래프. 2홉이면 노드가 상한을 넘는다. */
  const wide = (): GraphDocument => {
    const nodes = [node('root')]
    const edges: GraphEdge[] = []
    for (let i = 0; i < 20; i += 1) {
      nodes.push(node(`mid${i}`))
      edges.push({ from: 'root', to: `mid${i}`, confidence: 'Certain' })
      for (let j = 0; j < 20; j += 1) {
        nodes.push(node(`leaf${i}-${j}`))
        edges.push({ from: `mid${i}`, to: `leaf${i}-${j}`, confidence: 'Certain' })
      }
    }

    return { ...graph, nodes, edges, classes: [], entryPoints: [] }
  }

  it('상한을 넘으면 더 넣지 않고 남은 수를 알려준다', () => {
    const window = getDrilldownWindow(wide(), 'root')

    expect(window.nodes.length).toBeLessThanOrEqual(MaxWindowNodes)
    expect(window.hiddenCount).toBeGreaterThan(0)
  })

  it('경로 창은 진입점에서 대상까지를 모두 담는다', () => {
    const window = getPathWindow(wide(), 'root', ['root', 'mid3', 'leaf3-7'])
    const ids = new Set(window.nodes.map((item) => item.id))

    expect(ids.has('root')).toBe(true)
    expect(ids.has('mid3')).toBe(true)
    expect(ids.has('leaf3-7')).toBe(true)
  })
})

describe('1단계 팬아웃 상한과 호출자 창', () => {
  /** 컨트롤러 액션이 서비스·리포지터리 30개를 직접 부르는 그래프. 실전에서 흔하다. */
  const fanOut = (): GraphDocument => {
    const nodes = [node('entry')]
    const edges: GraphEdge[] = []
    for (let index = 0; index < 30; index += 1) {
      const kind = index % 2 === 0 ? 'Service' : 'Repository'
      nodes.push({ ...node(`child${index}`), kind })
      edges.push({ from: 'entry', to: `child${index}`, confidence: 'Certain' })
    }

    return { ...graph, nodes, edges, classes: [], entryPoints: [] }
  }

  /**
   * 첫 창에도 상한이 있어야 한다. 예전에는 `getInitialWindow`에 검사가 없어
   * 액션이 30개를 직접 부르면 1단계부터 화면이 무너졌다.
   */
  it('팬아웃이 크면 종류별 요약 노드로 접는다', () => {
    const window = getInitialWindow(fanOut(), 'entry')

    // 자식은 요약 노드로 대신한다. 창에 남는 것은 진입점뿐이다.
    expect(window.nodes.map((item) => item.id)).toEqual(['entry'])
    expect(window.groups.map((group) => group.kind).sort()).toEqual(['Repository', 'Service'])
    expect(window.groups.reduce((total, group) => total + group.nodeIds.length, 0)).toBe(30)
  })

  it('요약 노드를 펼치면 그 종류만 창에 들어온다', () => {
    const graphDocument = fanOut()
    const groups = getInitialWindow(graphDocument, 'entry').groups
    const service = groups.find((group) => group.kind === 'Service')!

    const window = getInitialWindow(graphDocument, 'entry', [service.id])

    expect(window.nodes).toHaveLength(1 + service.nodeIds.length)
    expect(window.groups.map((group) => group.kind)).toEqual(['Repository'])
  })

  /** "이 메서드를 부르는 세 경로"를 상세 패널의 pill 목록이 아니라 그래프에서 본다. */
  it('호출자 창은 부르는 쪽으로 2홉까지 자란다', () => {
    const window = getCallerWindow(graph, 'F', 2)

    expect(window.direction).toBe('up')
    expect(window.nodes.map((item) => item.id).sort()).toEqual(['C', 'D', 'F'])
    // 화살표는 언제나 "부르는 쪽 → 불리는 쪽"이다.
    expect(window.edges).toContainEqual({ from: 'D', to: 'F', confidence: 'Certain' })
  })

  /** 사이클이 있으면 무한히 돌지 않고 배지로 남긴다. */
  it('사이클에서 끝나고 그 사실을 남긴다', () => {
    const cyclic: GraphDocument = {
      ...graph,
      nodes: ['A', 'B'].map((id) => node(id)),
      edges: [
        { from: 'A', to: 'B', confidence: 'Certain' },
        { from: 'B', to: 'A', confidence: 'Certain' },
      ],
      classes: [],
      entryPoints: [],
    }

    const down = getInitialWindow(cyclic, 'A')
    expect(down.nodes.map((item) => item.id)).toEqual(['A', 'B'])
    expect(down.cycleNodeIds.has('A')).toBe(true)

    const up = getCallerWindow(cyclic, 'A', 3)
    expect(up.nodes.map((item) => item.id).sort()).toEqual(['A', 'B'])
  })
})
