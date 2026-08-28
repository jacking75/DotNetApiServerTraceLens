import type { GraphClass, GraphDocument, GraphEdge, GraphNode, GraphWindow } from '../types/graph'

function outgoingByNode(edges: GraphEdge[]): Map<string, GraphEdge[]> {
  const outgoing = new Map<string, GraphEdge[]>()
  for (const edge of edges) {
    const current = outgoing.get(edge.from) ?? []
    current.push(edge)
    outgoing.set(edge.from, current)
  }
  return outgoing
}

function buildWindow(graph: GraphDocument, focusNodeId: string, visibleIds: Set<string>): GraphWindow {
  const nodes = graph.nodes.filter((node) => visibleIds.has(node.id))
  const edges = graph.edges.filter((edge) => visibleIds.has(edge.from) && visibleIds.has(edge.to))
  const outgoing = outgoingByNode(graph.edges)
  // 아직 창에 없는 하위 호출이 남은 노드에만 펼침 표시를 붙인다.
  const branchNodeIds = new Set(
    nodes
      .filter((node) => (outgoing.get(node.id) ?? []).some((edge) => !visibleIds.has(edge.to)))
      .map((node) => node.id),
  )
  return { kind: 'callFlow', focusNodeId, nodes, edges, branchNodeIds }
}

/**
 * Level 1 창: 진입점에서 분기 없이 이어지는 직선 호출 체인을 따라가고,
 * 갈래가 나뉘는 첫 지점에서는 그 지점이 직접 호출하는 대상까지만 펼친다.
 * 그 아래 단계는 드릴다운으로 이어간다.
 */
export function getInitialWindow(graph: GraphDocument, entryPointId: string): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const visibleIds = new Set<string>([entryPointId])
  let current = entryPointId

  while (true) {
    const nextEdges = outgoing.get(current) ?? []
    if (nextEdges.length === 0) {
      break
    }

    if (nextEdges.length > 1) {
      for (const edge of nextEdges) {
        visibleIds.add(edge.to)
      }

      break
    }

    const next = nextEdges[0].to
    if (visibleIds.has(next)) {
      break
    }

    visibleIds.add(next)
    current = next
  }

  return buildWindow(graph, entryPointId, visibleIds)
}

export function getDrilldownWindow(graph: GraphDocument, focusNodeId: string, maxHops = 2): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const visibleIds = new Set<string>([focusNodeId])
  const queue: Array<{ id: string; depth: number }> = [{ id: focusNodeId, depth: 0 }]

  while (queue.length > 0) {
    const current = queue.shift()
    if (!current || current.depth >= maxHops) {
      continue
    }

    for (const edge of outgoing.get(current.id) ?? []) {
      if (!visibleIds.has(edge.to)) {
        visibleIds.add(edge.to)
        queue.push({ id: edge.to, depth: current.depth + 1 })
      }
    }
  }

  return buildWindow(graph, focusNodeId, visibleIds)
}

/** 클래스를 메서드 노드와 같은 모양으로 만들어서 코드 패널·질문 패널이 클래스 선언을 그대로 다루게 한다. */
function toClassNode(graphClass: GraphClass): GraphNode {
  return {
    id: graphClass.id,
    kind: graphClass.kind,
    displayName: graphClass.name,
    namespace: graphClass.namespace,
    classId: graphClass.id,
    file: graphClass.file,
    startLine: graphClass.startLine,
    endLine: graphClass.endLine,
  }
}

/** 메서드 호출 엣지를 클래스 단위로 접는다. 같은 클래스 안의 호출은 관계도에서 의미가 없으므로 뺀다. */
function collapseEdgesToClasses(graph: GraphDocument): GraphEdge[] {
  const ownerOf = new Map(graph.nodes.map((node) => [node.id, node.classId]))
  const classEdges = new Map<string, GraphEdge>()
  for (const edge of graph.edges) {
    const from = ownerOf.get(edge.from)
    const to = ownerOf.get(edge.to)
    if (!from || !to || from === to) {
      continue
    }

    classEdges.set(`${from} -> ${to}`, { from, to })
  }

  return [...classEdges.values()]
}

/**
 * 클래스 중심 관계도: 고른 클래스와, 그 클래스를 호출하거나 그 클래스가 호출하는 클래스만 1홉으로 보여준다.
 * 노드가 클래스라서 메서드가 하나도 없는 클래스(상수 모음 등)도 화면에 뜨고 소스를 볼 수 있다.
 */
export function getClassRelationWindow(graph: GraphDocument, classId: string): GraphWindow {
  const classEdges = collapseEdgesToClasses(graph)
  const degrees = new Map<string, number>()
  for (const edge of classEdges) {
    degrees.set(edge.from, (degrees.get(edge.from) ?? 0) + 1)
    degrees.set(edge.to, (degrees.get(edge.to) ?? 0) + 1)
  }

  const visibleIds = new Set<string>([classId])
  const shownDegrees = new Map<string, number>()
  const visibleEdges = classEdges.filter((edge) => edge.from === classId || edge.to === classId)
  for (const edge of visibleEdges) {
    visibleIds.add(edge.from)
    visibleIds.add(edge.to)
    shownDegrees.set(edge.from, (shownDegrees.get(edge.from) ?? 0) + 1)
    shownDegrees.set(edge.to, (shownDegrees.get(edge.to) ?? 0) + 1)
  }

  const nodes = graph.classes.filter((item) => visibleIds.has(item.id)).map(toClassNode)
  const renderedIds = new Set(nodes.map((node) => node.id))
  const branchNodeIds = new Set(
    nodes
      .filter((node) => (degrees.get(node.id) ?? 0) > (shownDegrees.get(node.id) ?? 0))
      .map((node) => node.id),
  )

  return {
    kind: 'classRelations',
    focusNodeId: classId,
    nodes,
    // 클래스 목록에 없는 클래스로 이어지는 엣지는 그릴 노드가 없으므로 뺀다.
    edges: visibleEdges.filter((edge) => renderedIds.has(edge.from) && renderedIds.has(edge.to)),
    branchNodeIds,
  }
}

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}
