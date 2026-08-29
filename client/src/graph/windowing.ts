import type { GraphDocument, GraphEdge, GraphNode, GraphWindow } from '../types/graph'

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

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}
