import { describe, expect, it } from 'vitest'
import { searchGraph } from './search'
import type { GraphClass, GraphDocument, GraphNode } from '../types/graph'

function node(id: string, displayName: string): GraphNode {
  return {
    id,
    displayName,
    kind: 'Method',
    classId: 'Shop.OrderService',
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
    },
  ],
  classes: [
    {
      id: 'Shop.OrderService',
      name: 'OrderService',
      namespace: 'Shop',
      members: [
        { memberKind: 'Method', name: 'CreateOrderAsync', signature: 'CreateOrderAsync(int)', nodeId: null },
        { memberKind: 'Method', name: 'TryCreateOrderAsyncCore', signature: 'TryCreateOrderAsyncCore(int)', nodeId: null },
      ],
    } as unknown as GraphClass,
  ],
  nodes: [node('Shop.OrderService.CreateOrderAsync(int)', 'OrderService.CreateOrderAsync(int)')],
} as unknown as GraphDocument

describe('searchGraph', () => {
  /**
   * 티켓에 적힌 메서드 이름으로 찾을 수 없으면 신입은 파일을 하나씩 열어야 한다.
   * 노드가 없는 멤버(인터페이스 선언 등)도 찾을 수 있어야 한다.
   */
  it('메서드 이름으로 노드와 멤버를 모두 찾는다', () => {
    const result = searchGraph(graph, 'createorder')

    // 실행 가능한 노드가 있으면 그것을 쓰고, 같은 멤버를 두 번 보여 주지 않는다.
    expect(result.methods.map((hit) => hit.label)).toContain('OrderService.CreateOrderAsync(int)')
    // 노드가 없는 멤버도 찾을 수 있어야 한다(인터페이스 선언 등).
    expect(result.methods.map((hit) => hit.label)).toContain('TryCreateOrderAsyncCore')
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
    expect(searchGraph(graph, '/api/orders').entryPoints).toHaveLength(1)
    expect(searchGraph(graph, 'post').entryPoints).toHaveLength(1)
  })

  it('상한을 넘으면 잘랐다는 사실을 남긴다', () => {
    const result = searchGraph(graph, 'createorder', 1)

    expect(result.methods).toHaveLength(1)
    expect(result.truncated).toBe(true)
  })

  it('빈 질의나 분석 전에는 아무것도 돌려주지 않는다', () => {
    expect(searchGraph(graph, '   ').methods).toEqual([])
    expect(searchGraph(null, 'order').methods).toEqual([])
  })
})
