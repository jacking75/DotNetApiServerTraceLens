import type { EntryPoint, GraphDocument, NodeKind } from '../types/graph'
import type { ClassRelation } from './relations'
import { shortenNamespace } from './classDiagram'
import { layoutTree, type DiagramLayout, type TreeDirection } from './treeLayout'

/** 흐름도 박스 안의 한 줄. 컨트롤러는 라우트를, 진입점 흐름은 메서드를 줄로 그린다. */
export interface FlowRow {
  key: string
  label: string
  httpMethod?: string
  badge?: string
  rowKind: 'route' | 'method'
  /** 자기 자신을 부르는 재귀 호출. 줄에 표시만 하고 선은 그리지 않는다. */
  recursive?: boolean
}

export interface FlowBox {
  id: string
  title: string
  subtitle: string
  kind: NodeKind
  w: number
  h: number
  rows: FlowRow[]
}

/** 흐름도의 선. 메서드 줄에 끝점을 붙이려고 줄 키를 함께 담는다. */
export interface FlowRelation extends ClassRelation {
  fromRow?: string
  toRow?: string
}

export interface FlowView {
  boxes: FlowBox[]
  relations: FlowRelation[]
  fixedLayers?: Map<string, number>
  meta: string
  /** 깊이 제한에 걸려 화면에서 빠진 메서드 수. */
  hiddenCount: number
}

const headHeight = 32
const subtitleHeight = 18
export const flowRowHeight = 22

export function flowBoxHeight(box: Pick<FlowBox, 'subtitle' | 'rows'>): number {
  return headHeight + (box.subtitle ? subtitleHeight : 0) + (box.rows.length > 0 ? box.rows.length * flowRowHeight + 1 : 0)
}

/** 박스 안 n번째 줄의 세로 중심. 선을 그 줄에 붙일 때 쓴다. */
export function flowRowCenter(box: FlowBox, rowKey: string, boxY: number): number {
  const index = box.rows.findIndex((row) => row.key === rowKey)
  if (index < 0) return boxY + flowBoxHeight(box) / 2
  return boxY + headHeight + (box.subtitle ? subtitleHeight : 0) + 1 + index * flowRowHeight + flowRowHeight / 2
}

function buildAdjacency(graph: GraphDocument) {
  const outgoing = new Map<string, string[]>()
  for (const edge of graph.edges) {
    const list = outgoing.get(edge.from)
    if (list) list.push(edge.to)
    else outgoing.set(edge.from, [edge.to])
  }

  return outgoing
}

/**
 * 전체 진입점 지도. 컨트롤러 박스에는 라우트를 줄로 붙이고,
 * 나머지 클래스는 이름만 있는 작은 박스로 두고 호출 엣지를 클래스 단위로 접어 잇는다.
 */
export function buildEntryMap(
  graph: GraphDocument,
  entryPoints: EntryPoint[],
  namespacePrefix: string,
): FlowView {
  const classOf = new Map(graph.nodes.map((node) => [node.id, node.classId]))
  const entryByNodeId = new Map(entryPoints.map((entry) => [entry.nodeId, entry]))
  const controllerIds = new Set(graph.classes.filter((item) => item.kind === 'Controller').map((item) => item.id))

  const touched = new Set<string>()
  for (const edge of graph.edges) {
    const from = classOf.get(edge.from)
    const to = classOf.get(edge.to)
    if (!from || !to || from === to) continue
    touched.add(from)
    touched.add(to)
  }

  const boxes: FlowBox[] = []
  for (const item of graph.classes) {
    const isController = controllerIds.has(item.id)
    if (!isController && !touched.has(item.id)) continue

    const rows: FlowRow[] = isController
      ? item.methodIds
        .filter((methodId) => entryByNodeId.has(methodId))
        .map((methodId) => {
          const entry = entryByNodeId.get(methodId)!
          return {
            key: methodId,
            label: entry.route,
            httpMethod: entry.httpMethod,
            badge: '→',
            rowKind: 'route' as const,
          }
        })
      : []

    boxes.push({
      id: item.id,
      title: item.name,
      subtitle: isController
        ? shortenNamespace(item.namespace, namespacePrefix)
        : `${item.kind} · 메서드 ${item.methodIds.length}`,
      kind: item.kind,
      w: isController ? 234 : 200,
      h: flowBoxHeight({ subtitle: 'x', rows }),
      rows,
    })
  }

  const present = new Set(boxes.map((box) => box.id))
  const relations: FlowRelation[] = []
  const seen = new Set<string>()
  for (const edge of graph.edges) {
    const from = classOf.get(edge.from)
    const to = classOf.get(edge.to)
    if (!from || !to || from === to || !present.has(from) || !present.has(to)) continue
    const fromRow = controllerIds.has(from) && entryByNodeId.has(edge.from) ? edge.from : undefined
    const key = `${fromRow ?? from}>${to}`
    if (seen.has(key)) continue
    seen.add(key)
    relations.push({ from, to, type: 'depend', fromRow })
  }

  return {
    boxes,
    relations,
    meta: `진입점 ${entryPoints.length}개 · 클래스 ${boxes.length}개`,
    hiddenCount: 0,
  }
}

/**
 * 진입점 흐름. 선택한 진입점에서 BFS로 깊이를 재고 `depth <= limit`인 메서드만 남긴 뒤,
 * 클래스로 묶어 박스를 만들고 선은 메서드 줄 ↔ 메서드 줄로 잇는다.
 * 방문 집합으로 순환을 막고, 자기 자신을 부르는 재귀는 줄에 표시만 한다.
 */
export function buildEntryFlow(
  graph: GraphDocument,
  entryNodeId: string,
  depthLimit: number,
  entryPoints: EntryPoint[],
  namespacePrefix: string,
): FlowView {
  const outgoing = buildAdjacency(graph)
  const nodeById = new Map(graph.nodes.map((node) => [node.id, node]))
  const classById = new Map(graph.classes.map((item) => [item.id, item]))
  const entryNodeIds = new Set(entryPoints.map((entry) => entry.nodeId))

  const depths = new Map<string, number>([[entryNodeId, 0]])
  const queue: string[] = [entryNodeId]
  while (queue.length > 0) {
    const current = queue.shift()!
    for (const next of outgoing.get(current) ?? []) {
      if (depths.has(next)) continue
      depths.set(next, depths.get(current)! + 1)
      queue.push(next)
    }
  }

  const visible = new Set([...depths].filter(([, depth]) => depth <= depthLimit).map(([id]) => id))
  const byClass = new Map<string, string[]>()
  for (const id of visible) {
    const node = nodeById.get(id)
    if (!node) continue
    const list = byClass.get(node.classId)
    if (list) list.push(id)
    else byClass.set(node.classId, [id])
  }

  const boxes: FlowBox[] = []
  const fixedLayers = new Map<string, number>()
  for (const [classId, methodIds] of byClass) {
    const graphClass = classById.get(classId)
    const sorted = [...methodIds].sort((left, right) => (depths.get(left)! - depths.get(right)!) || left.localeCompare(right))
    fixedLayers.set(classId, Math.min(...methodIds.map((id) => depths.get(id)!)))

    const rows: FlowRow[] = sorted.map((methodId) => {
      const callees = outgoing.get(methodId) ?? []
      const hidden = callees.filter((id) => !visible.has(id)).length
      const recursive = callees.includes(methodId)
      return {
        key: methodId,
        label: `${nodeById.get(methodId)?.displayName.split('.').pop() ?? methodId}${entryNodeIds.has(methodId) ? '  ▸' : ''}`,
        badge: [recursive ? '↺' : '', hidden > 0 ? `+${hidden}` : ''].filter(Boolean).join(' '),
        rowKind: 'method' as const,
        recursive,
      }
    })

    boxes.push({
      id: classId,
      title: graphClass?.name ?? classId,
      subtitle: shortenNamespace(graphClass?.namespace ?? '', namespacePrefix),
      kind: graphClass?.kind ?? 'Method',
      w: 216,
      h: flowBoxHeight({ subtitle: 'x', rows }),
      rows,
    })
  }

  const relations: FlowRelation[] = []
  const seen = new Set<string>()
  for (const edge of graph.edges) {
    if (!visible.has(edge.from) || !visible.has(edge.to)) continue
    const from = nodeById.get(edge.from)?.classId
    const to = nodeById.get(edge.to)?.classId
    if (!from || !to || from === to) continue
    const key = `${edge.from}>${edge.to}`
    if (seen.has(key)) continue
    seen.add(key)
    relations.push({ from, to, type: 'depend', fromRow: edge.from, toRow: edge.to })
  }

  const hiddenCount = depths.size - visible.size
  return {
    boxes,
    relations,
    fixedLayers,
    meta: `메서드 ${visible.size}개 · 클래스 ${boxes.length}개${hiddenCount > 0 ? ` · 깊이 밖 ${hiddenCount}개` : ''}`,
    hiddenCount,
  }
}

/**
 * 흐름도 배치. 클래스 다이어그램과 같은 계층 엔진(더미·바리센터 포함)을 쓰고,
 * 선의 끝점만 메서드 줄에 붙도록 anchor를 넘긴다.
 */
export function layoutFlow(view: FlowView, direction: TreeDirection = 'LR'): DiagramLayout<FlowRelation> {
  const boxById = new Map(view.boxes.map((box) => [box.id, box]))
  return layoutTree(
    view.boxes.map((box) => ({ id: box.id, w: box.w, h: box.h })),
    view.relations,
    {
      direction,
      fixedLayers: view.fixedLayers,
      gapMain: direction === 'LR' ? 80 : 56,
      gapCross: 18,
      anchor: (relation, box, side) => {
        const flowBox = boxById.get(box.id)
        const rowKey = side === 'from' ? relation.fromRow : relation.toRow
        const outgoing = side === 'from'
        if (direction === 'TB') {
          return { x: box.x + box.w / 2, y: outgoing ? box.y + box.h : box.y }
        }

        const y = flowBox && rowKey ? flowRowCenter(flowBox, rowKey, box.y) : box.y + box.h / 2
        return { x: outgoing ? box.x + box.w : box.x, y }
      },
    })
}
