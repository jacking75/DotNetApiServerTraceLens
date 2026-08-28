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

function buildWindow(
  graph: GraphDocument,
  focusNodeId: string,
  visibleIds: Set<string>,
  group: { classId: string; label: string } | null = null,
): GraphWindow {
  const nodes = graph.nodes.filter((node) => visibleIds.has(node.id))
  const edges = graph.edges.filter((edge) => visibleIds.has(edge.from) && visibleIds.has(edge.to))
  const outgoing = outgoingByNode(graph.edges)
  const branchNodeIds = new Set(
    nodes.filter((node) => (outgoing.get(node.id)?.length ?? 0) > 1).map((node) => node.id),
  )
  return {
    focusNodeId,
    nodes,
    edges,
    branchNodeIds,
    groupClassId: group?.classId ?? null,
    groupLabel: group?.label ?? null,
  }
}

export function getInitialWindow(graph: GraphDocument, entryPointId: string): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const visibleIds = new Set<string>([entryPointId])
  let current = entryPointId

  while (true) {
    const nextEdges = outgoing.get(current) ?? []
    if (nextEdges.length !== 1) {
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

/**
 * 클래스 중심 창: 해당 클래스의 메서드 노드 전부와, 그 메서드들의 직접 호출자/피호출자 1홉을 보여준다.
 * 포커스 ID로는 노드 ID와 겹치지 않는 클래스 ID를 쓴다.
 */
export function getClassWindow(graph: GraphDocument, classId: string): GraphWindow {
  const memberIds = new Set(graph.nodes.filter((node) => node.classId === classId).map((node) => node.id))
  const visibleIds = new Set<string>(memberIds)
  for (const edge of graph.edges) {
    if (memberIds.has(edge.from)) {
      visibleIds.add(edge.to)
    }
    if (memberIds.has(edge.to)) {
      visibleIds.add(edge.from)
    }
  }

  const label = graph.classes.find((item) => item.id === classId)?.name ?? classId
  return buildWindow(graph, classId, visibleIds, { classId, label })
}

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}

export function findClassMemberNodes(graph: GraphDocument, classId: string): GraphNode[] {
  return graph.nodes.filter((node) => node.classId === classId)
}
