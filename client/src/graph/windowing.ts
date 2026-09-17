import type { GraphDocument, GraphEdge, GraphNode, GraphWindow } from '../types/graph'

/**
 * 한 창에 그리는 노드 상한. 넘으면 남은 수를 알려 주고 더 펼치지 않는다.
 * 창을 무한정 넓히지 않는 것이 포커스 기반 윈도잉의 전제다(CLAUDE.md 제약 1).
 */
export const MaxWindowNodes = 120

function outgoingByNode(edges: GraphEdge[]): Map<string, GraphEdge[]> {
  const outgoing = new Map<string, GraphEdge[]>()
  for (const edge of edges) {
    const current = outgoing.get(edge.from) ?? []
    current.push(edge)
    outgoing.set(edge.from, current)
  }
  return outgoing
}

function buildWindow(
  graph: GraphDocument,
  focusNodeId: string,
  visibleIds: Set<string>,
  hiddenCount = 0,
): GraphWindow {
  const nodes = graph.nodes.filter((node) => visibleIds.has(node.id))
  const edges = graph.edges.filter((edge) => visibleIds.has(edge.from) && visibleIds.has(edge.to))
  const outgoing = outgoingByNode(graph.edges)
  // 아직 창에 없는 하위 호출이 남은 노드에만 펼침 표시를 붙인다.
  const branchNodeIds = new Set(
    nodes
      .filter((node) => (outgoing.get(node.id) ?? []).some((edge) => !visibleIds.has(edge.to)))
      .map((node) => node.id),
  )
  return { kind: 'callFlow', focusNodeId, nodes, edges, branchNodeIds, hiddenCount }
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

/**
 * 포커스에서 `maxHops`까지 펼친다(기본 2홉).
 * 노드가 {@link MaxWindowNodes}를 넘으면 더 넣지 않고 남은 수만 세어 창에 알린다.
 */
export function getDrilldownWindow(graph: GraphDocument, focusNodeId: string, maxHops = 2): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const visibleIds = new Set<string>([focusNodeId])
  const skipped = new Set<string>()
  const queue: Array<{ id: string; depth: number }> = [{ id: focusNodeId, depth: 0 }]

  while (queue.length > 0) {
    const current = queue.shift()
    if (!current || current.depth >= maxHops) {
      continue
    }

    for (const edge of outgoing.get(current.id) ?? []) {
      if (visibleIds.has(edge.to)) continue
      if (visibleIds.size >= MaxWindowNodes) {
        skipped.add(edge.to)
        continue
      }

      visibleIds.add(edge.to)
      queue.push({ id: edge.to, depth: current.depth + 1 })
    }
  }

  return buildWindow(graph, focusNodeId, visibleIds, skipped.size)
}

/**
 * 진입점에서 대상 노드까지의 경로를 모두 펼친 창.
 * "변경 영향" 목록에서 고른 대상이 창 밖에 있지 않도록, 그 경로의 노드만 정확히 넣는다.
 */
export function getPathWindow(graph: GraphDocument, focusNodeId: string, pathNodeIds: readonly string[]): GraphWindow {
  const known = new Set(graph.nodes.map((node) => node.id))
  const visibleIds = new Set<string>([focusNodeId])
  for (const id of pathNodeIds) {
    if (known.has(id) && visibleIds.size < MaxWindowNodes) visibleIds.add(id)
  }

  // 경로 주변을 한 홉만 더 보여 준다. 어디서 갈라지는지 보이지 않으면 경로만 떠 있는 그림이 된다.
  const outgoing = outgoingByNode(graph.edges)
  for (const id of [...visibleIds]) {
    for (const edge of outgoing.get(id) ?? []) {
      if (visibleIds.size >= MaxWindowNodes) break
      visibleIds.add(edge.to)
    }
  }

  return buildWindow(graph, focusNodeId, visibleIds)
}

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}
