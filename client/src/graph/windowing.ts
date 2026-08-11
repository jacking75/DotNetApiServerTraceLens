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
  const branchNodeIds = new Set(
    nodes.filter((node) => (outgoing.get(node.id)?.length ?? 0) > 1).map((node) => node.id),
  )
  return { focusNodeId, nodes, edges, branchNodeIds }
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

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}
