import type { GraphDocument, GraphEdge, GraphNode, GraphWindow, NodeKind } from '../types/graph'

/**
 * 한 창에 그리는 노드 상한. 넘으면 남은 수를 알려 주고 더 펼치지 않는다.
 * 창을 무한정 넓히지 않는 것이 포커스 기반 윈도잉의 전제다(CLAUDE.md 제약 1).
 */
export const MaxWindowNodes = 120

/**
 * 한 노드가 직접 부르는 대상을 이만큼 넘게 그리면 창이 무너진다.
 * 넘치면 종류별 요약 노드로 접고 누르면 그 종류만 펼친다.
 */
export const MaxFanOut = 12

function outgoingByNode(edges: GraphEdge[]): Map<string, GraphEdge[]> {
  const outgoing = new Map<string, GraphEdge[]>()
  for (const edge of edges) {
    const current = outgoing.get(edge.from) ?? []
    current.push(edge)
    outgoing.set(edge.from, current)
  }
  return outgoing
}

function incomingByNode(edges: GraphEdge[]): Map<string, GraphEdge[]> {
  const incoming = new Map<string, GraphEdge[]>()
  for (const edge of edges) {
    const current = incoming.get(edge.to) ?? []
    current.push(edge)
    incoming.set(edge.to, current)
  }
  return incoming
}

interface BuildOptions {
  hiddenCount?: number
  /** 접어 둔 팬아웃. 요약 노드를 만들 재료다. */
  groups?: FanOutGroup[]
  direction?: 'down' | 'up'
  /** 방문 집합에 걸려 더 따라가지 않은 노드. 화면이 `↺` 배지를 붙인다. */
  cycleNodeIds?: Set<string>
}

/** 한 부모 아래에서 종류별로 접은 자식들. */
export interface FanOutGroup {
  id: string
  parentNodeId: string
  kind: NodeKind
  nodeIds: string[]
}

function buildWindow(
  graph: GraphDocument,
  focusNodeId: string,
  visibleIds: Set<string>,
  options: BuildOptions = {},
): GraphWindow {
  const { hiddenCount = 0, groups = [], direction = 'down', cycleNodeIds = new Set<string>() } = options
  const nodes = graph.nodes.filter((node) => visibleIds.has(node.id))
  const edges = graph.edges.filter((edge) => visibleIds.has(edge.from) && visibleIds.has(edge.to))
  const outgoing = outgoingByNode(graph.edges)
  const incoming = incomingByNode(graph.edges)
  const adjacent = direction === 'up' ? incoming : outgoing
  const grouped = new Set(groups.flatMap((group) => group.nodeIds))
  // 아직 창에 없는 관계가 남은 노드에만 펼침 표시를 붙인다(요약 노드로 접은 것은 뺀다).
  const branchNodeIds = new Set(
    nodes
      .filter((node) => (adjacent.get(node.id) ?? []).some((edge) => {
        const other = direction === 'up' ? edge.from : edge.to
        return !visibleIds.has(other) && !grouped.has(other)
      }))
      .map((node) => node.id),
  )
  return {
    kind: 'callFlow',
    direction,
    focusNodeId,
    nodes,
    edges,
    branchNodeIds,
    hiddenCount,
    groups,
    cycleNodeIds,
  }
}

/** 자식이 너무 많으면 종류별로 묶는다. 묶인 자식은 창에 넣지 않고 요약 노드 하나로 대신한다. */
function foldFanOut(
  parentNodeId: string,
  childIds: readonly string[],
  kindOf: (id: string) => NodeKind,
): { keep: string[]; groups: FanOutGroup[] } {
  if (childIds.length <= MaxFanOut) return { keep: [...childIds], groups: [] }

  const byKind = new Map<NodeKind, string[]>()
  for (const id of childIds) {
    const kind = kindOf(id)
    const list = byKind.get(kind)
    if (list) list.push(id)
    else byKind.set(kind, [id])
  }

  const keep: string[] = []
  const groups: FanOutGroup[] = []
  for (const [kind, ids] of byKind) {
    // 한두 개짜리 종류까지 접으면 오히려 읽기 어렵다. 그대로 둔다.
    if (ids.length <= 2) {
      keep.push(...ids)
      continue
    }

    groups.push({ id: `fanout:${parentNodeId}:${kind}`, parentNodeId, kind, nodeIds: ids })
  }

  return { keep, groups }
}

/**
 * Level 1 창: 진입점에서 분기 없이 이어지는 직선 호출 체인을 따라가고,
 * 갈래가 나뉘는 첫 지점에서는 그 지점이 직접 호출하는 대상까지만 펼친다.
 * 그 아래 단계는 드릴다운으로 이어간다.
 *
 * 팬아웃이 {@link MaxFanOut}을 넘으면 종류별 요약 노드로 접는다 —
 * 컨트롤러 액션이 서비스 30개를 직접 부르면 첫 창부터 읽을 수 없게 된다.
 */
export function getInitialWindow(
  graph: GraphDocument,
  entryPointId: string,
  expandedGroupIds: readonly string[] = [],
): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const kindById = new Map(graph.nodes.map((node) => [node.id, node.kind]))
  const kindOf = (id: string) => kindById.get(id) ?? 'Method'
  const visibleIds = new Set<string>([entryPointId])
  const cycleNodeIds = new Set<string>()
  let groups: FanOutGroup[] = []
  let hiddenCount = 0
  let current = entryPointId

  while (true) {
    const nextEdges = outgoing.get(current) ?? []
    if (nextEdges.length === 0) break

    if (nextEdges.length > 1) {
      const childIds = [...new Set(nextEdges.map((edge) => edge.to))]
      const folded = foldFanOut(current, childIds, kindOf)
      groups = folded.groups
      for (const id of folded.keep) {
        if (visibleIds.size >= MaxWindowNodes) {
          hiddenCount += 1
          continue
        }

        visibleIds.add(id)
      }

      // 펼친 요약 노드는 그 종류의 자식을 창에 되돌린다.
      for (const group of groups.filter((item) => expandedGroupIds.includes(item.id))) {
        for (const id of group.nodeIds) {
          if (visibleIds.size >= MaxWindowNodes) {
            hiddenCount += 1
            continue
          }

          visibleIds.add(id)
        }
      }

      groups = groups.filter((item) => !expandedGroupIds.includes(item.id))
      break
    }

    const next = nextEdges[0].to
    if (visibleIds.has(next)) {
      cycleNodeIds.add(next)
      break
    }

    if (visibleIds.size >= MaxWindowNodes) {
      hiddenCount += 1
      break
    }

    visibleIds.add(next)
    current = next
  }

  return buildWindow(graph, entryPointId, visibleIds, { hiddenCount, groups, cycleNodeIds })
}

/**
 * 포커스에서 `maxHops`까지 펼친다(기본 2홉).
 * 노드가 {@link MaxWindowNodes}를 넘으면 더 넣지 않고 남은 수만 세어 창에 알린다.
 */
export function getDrilldownWindow(graph: GraphDocument, focusNodeId: string, maxHops = 2): GraphWindow {
  const outgoing = outgoingByNode(graph.edges)
  const visibleIds = new Set<string>([focusNodeId])
  const skipped = new Set<string>()
  const cycleNodeIds = new Set<string>()
  const queue: Array<{ id: string; depth: number }> = [{ id: focusNodeId, depth: 0 }]

  while (queue.length > 0) {
    const current = queue.shift()
    if (!current || current.depth >= maxHops) {
      continue
    }

    for (const edge of outgoing.get(current.id) ?? []) {
      if (visibleIds.has(edge.to)) {
        // 이미 창에 있는 노드로 되돌아가는 호출이다. 사이클이 있다는 사실을 배지로 남긴다.
        if (edge.to === focusNodeId || edge.to === current.id) cycleNodeIds.add(edge.to)
        continue
      }

      if (visibleIds.size >= MaxWindowNodes) {
        skipped.add(edge.to)
        continue
      }

      visibleIds.add(edge.to)
      queue.push({ id: edge.to, depth: current.depth + 1 })
    }
  }

  return buildWindow(graph, focusNodeId, visibleIds, { hiddenCount: skipped.size, cycleNodeIds })
}

/**
 * 이 메서드를 부르는 쪽으로 자라는 창(기본 2홉).
 * "이 메서드를 부르는 세 경로"를 상세 패널의 목록이 아니라 그래프에서 본다.
 * 호출 엣지만 쓰므로 엣지 범위 제약(CLAUDE.md 제약 2) 안이다.
 */
export function getCallerWindow(graph: GraphDocument, nodeId: string, maxHops = 2): GraphWindow {
  const incoming = incomingByNode(graph.edges)
  const visibleIds = new Set<string>([nodeId])
  const skipped = new Set<string>()
  const cycleNodeIds = new Set<string>()
  const queue: Array<{ id: string; depth: number }> = [{ id: nodeId, depth: 0 }]

  while (queue.length > 0) {
    const current = queue.shift()
    if (!current || current.depth >= maxHops) continue

    for (const edge of incoming.get(current.id) ?? []) {
      if (visibleIds.has(edge.from)) {
        if (edge.from === nodeId || edge.from === current.id) cycleNodeIds.add(edge.from)
        continue
      }

      if (visibleIds.size >= MaxWindowNodes) {
        skipped.add(edge.from)
        continue
      }

      visibleIds.add(edge.from)
      queue.push({ id: edge.from, depth: current.depth + 1 })
    }
  }

  return buildWindow(graph, nodeId, visibleIds, {
    hiddenCount: skipped.size,
    direction: 'up',
    cycleNodeIds,
  })
}

/**
 * 진입점에서 대상 노드까지의 경로를 모두 펼친 창.
 * "변경 영향" 목록에서 고른 대상이 창 밖에 있지 않도록, 그 경로의 노드만 정확히 넣는다.
 */
export function getPathWindow(graph: GraphDocument, focusNodeId: string, pathNodeIds: readonly string[]): GraphWindow {
  const known = new Set(graph.nodes.map((node) => node.id))
  const visibleIds = new Set<string>([focusNodeId])
  let hiddenCount = 0
  for (const id of pathNodeIds) {
    if (!known.has(id)) continue
    if (visibleIds.size >= MaxWindowNodes) {
      hiddenCount += 1
      continue
    }

    visibleIds.add(id)
  }

  // 경로 주변을 한 홉만 더 보여 준다. 어디서 갈라지는지 보이지 않으면 경로만 떠 있는 그림이 된다.
  const outgoing = outgoingByNode(graph.edges)
  for (const id of [...visibleIds]) {
    for (const edge of outgoing.get(id) ?? []) {
      if (visibleIds.size >= MaxWindowNodes) break
      visibleIds.add(edge.to)
    }
  }

  return buildWindow(graph, focusNodeId, visibleIds, { hiddenCount })
}

export function findNode(graph: GraphDocument, nodeId: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === nodeId)
}
