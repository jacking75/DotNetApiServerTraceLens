import { describe, expect, it } from 'vitest'
import { parseQuery, resolveRecentHits, searchGraph } from './search'
import type { GraphClass, GraphDocument, GraphNode } from '../types/graph'

describe('최근 검색 복원', () => {
  it('현재 이름으로 복원하고 중복·삭제·잘못된 종류는 버린다', () => {
    const entry = graph.entryPoints[0]
    const key = `entryPoint:${entry.endpointId}`
    const hits = resolveRecentHits(graph, [key, key, 'method:missing', 'bad:value', 'invalid'])
    expect(hits).toEqual([{ kind: 'entryPoint', id: entry.endpointId, label: 'POST /api/orders', detail: entry.handlerDisplayName }])
    expect(resolveRecentHits(null, [key])).toEqual([])
  })
})

function node(id: string, displayName: string, classId = 'Shop.OrderService'): GraphNode {
  return {
    id,
    displayName,
    kind: 'Method',
    classId,
    namespace: 'Shop',
    file: 'src/OrderService.cs',
    startLine: 1,
    endLine: 2,
    accessibility: 'Public',
    returnType: 'Task',
    isStatic: false,
    callable: 'Method',
    stableKey: id,
    parameterTypes: ['int'],
    signatureFingerprint: '',
    bodyFingerprint: '',
  } as unknown as GraphNode
}

const graph = {
  entryPoints: [
    {
      nodeId: 'Shop.OrdersController.Create()',
      httpMethod: 'POST',
      route: '/api/orders',
      endpointId: 'Shop|POST|/api/orders|Create',
      normalizedRoute: '/api/orders',
      source: 'Controller',
      handlerKind: 'Method',
      confidence: 'Certain',
      registrationFile: 'src/OrdersController.cs',
      registrationLine: 10,
      handlerDisplayName: 'OrdersController.Create()',
      projectKey: 'Shop',
      notes: [],
      groupKey: '',
      hiddenFromApiExplorer: false,
      authorization: null,
    },
    {
      nodeId: 'Shop.OrdersController.List()',
      httpMethod: 'GET',
      route: '/api/orders',
      endpointId: 'Shop|GET|/api/orders|List',
      normalizedRoute: '/api/orders',
      source: 'Controller',
      handlerKind: 'Method',
      confidence: 'Certain',
      registrationFile: 'src/OrdersController.cs',
      registrationLine: 20,
      handlerDisplayName: 'OrdersController.List()',
      projectKey: 'Shop',
      notes: [],
      groupKey: '',
      hiddenFromApiExplorer: false,
      authorization: null,
    },
  ],
  classes: [
    {
      id: 'Shop.IInventoryRepository',
      name: 'IInventoryRepository',
      namespace: 'Shop',
      typeKind: 'Interface',
      interfaceIds: [],
      members: [{ memberKind: 'Method', name: 'ReserveAsync', signature: 'ReserveAsync(int)', nodeId: null }],
    } as unknown as GraphClass,
    {
      id: 'Shop.InventoryRepository',
      name: 'InventoryRepository',
      namespace: 'Shop',
      typeKind: 'Class',
      interfaceIds: ['Shop.IInventoryRepository'],
      members: [{ memberKind: 'Method', name: 'ReserveAsync', signature: 'ReserveAsync(int)', nodeId: null }],
    } as unknown as GraphClass,
    {
      id: 'Shop.OrderService',
      name: 'OrderService',
      namespace: 'Shop',
      typeKind: 'Class',
      interfaceIds: [],
      members: [
        { memberKind: 'Method', name: 'CreateOrderAsync', signature: 'CreateOrderAsync(int)', nodeId: null },
        { memberKind: 'Method', name: 'TryCreateOrderAsyncCore', signature: 'TryCreateOrderAsyncCore(int)', nodeId: null },
      ],
    } as unknown as GraphClass,
  ],
  nodes: [
    node('Shop.OrderService.CreateOrderAsync(int)', 'OrderService.CreateOrderAsync(int)'),
    node('Shop.InventoryRepository.ReserveAsync(int)', 'InventoryRepository.ReserveAsync(int)', 'Shop.InventoryRepository'),
  ],
  dataAccess: {
    rules: [{ id: 'COMP-001', name: '보상 처리 없음' }],
    findings: [],
  },
  middleware: { rules: [], findings: [] },
} as unknown as GraphDocument

describe('searchGraph', () => {
  /**
   * 고른 결과가 "이 메서드는 마지막 분석 결과에 없다"로 끝나면 찾기를 믿지 않게 된다.
   * 메서드 그룹에는 실행 가능한 그래프 노드만 넣는다.
   */
  it('노드가 없는 멤버는 메서드 그룹에 넣지 않는다', () => {
    const result = searchGraph(graph, 'createorder')

    expect(result.methods.map((hit) => hit.label)).toContain('OrderService.CreateOrderAsync(int)')
    expect(result.methods.map((hit) => hit.label)).not.toContain('TryCreateOrderAsyncCore')
    // 대신 클래스 결과의 부제로 남는다 — 찾을 수는 있되 막다른 길로 보내지 않는다.
    expect(result.classes.find((hit) => hit.id === 'Shop.OrderService')?.detail).toContain('TryCreateOrderAsyncCore')
  })

  /** 인터페이스 멤버를 고르면 구현 노드로 가야 한다. 인터페이스 선언에는 볼 것이 없다. */
  it('인터페이스 멤버는 구현 노드를 달아 준다', () => {
    const result = searchGraph(graph, 'ReserveAsync')

    const hit = result.methods.find((item) => item.label === 'IInventoryRepository.ReserveAsync')
    expect(hit).toBeTruthy()
    expect(hit!.id).toBe('Shop.InventoryRepository.ReserveAsync(int)')
    expect(hit!.implementations).toEqual([
      { id: 'Shop.InventoryRepository.ReserveAsync(int)', label: 'InventoryRepository.ReserveAsync' },
    ])
  })

  /**
   * 접두 일치가 먼저 와야 한다. `TryCreateOrderAsyncCore`가 위에 오면 찾은 보람이 없다.
   * 순위는 한정자를 뺀 홑이름으로 매긴다 — 노드 라벨은 `Class.Method(args)` 형태다.
   */
  it('접두 일치를 포함 일치보다 먼저 보여 준다', () => {
    const result = searchGraph(graph, 'CreateOrderAsync')

    expect(result.methods[0].label).toBe('OrderService.CreateOrderAsync(int)')
  })

  it('라우트와 동사로 진입점을 찾는다', () => {
    expect(searchGraph(graph, '/api/orders').entryPoints).toHaveLength(2)
    expect(searchGraph(graph, 'post').entryPoints).toHaveLength(1)
  })

  /** 접두사 필터가 없으면 결과 20개가 한 종류로 차 버린다. */
  it('접두사로 범위를 좁힌다', () => {
    expect(parseQuery('GET /orders')).toEqual({ scope: 'entryPoint', httpMethod: 'GET', needle: '/orders' })
    expect(parseQuery('ns:Shop')).toEqual({ scope: 'namespace', httpMethod: '', needle: 'shop' })
    expect(parseQuery('rule:COMP')).toEqual({ scope: 'rule', httpMethod: '', needle: 'comp' })

    const verbs = searchGraph(graph, 'GET /api/orders')
    expect(verbs.entryPoints.map((hit) => hit.label)).toEqual(['GET /api/orders'])
    expect(verbs.methods).toEqual([])

    expect(searchGraph(graph, 'rule:COMP').rules.map((hit) => hit.id)).toEqual(['COMP-001'])
    expect(searchGraph(graph, 'file:OrderService').files.map((hit) => hit.detail)).toEqual(['src/OrderService.cs'])
  })

  it('상한을 넘으면 잘랐다는 사실을 남긴다', () => {
    const result = searchGraph(graph, '/api/orders', 1)

    expect(result.entryPoints).toHaveLength(1)
    expect(result.truncated).toBe(true)
  })

  it('빈 질의나 분석 전에는 아무것도 돌려주지 않는다', () => {
    expect(searchGraph(graph, '   ').methods).toEqual([])
    expect(searchGraph(null, 'order').methods).toEqual([])
  })
})
