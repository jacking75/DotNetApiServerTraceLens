import { bezierBetween, smoothPath, type Point, type Rect } from './geometry'
import type { ClassRelation, RelationType } from './relations'

export type TreeDirection = 'LR' | 'TB'

export interface LayoutNode {
  id: string
  w: number
  h: number
}

export interface PlacedBox extends Rect {
  id: string
}

export interface RelationPath<T extends ClassRelation = ClassRelation> {
  type: RelationType
  from: string
  to: string
  d: string
  /** 층을 거슬러 올라가는 선(순환)이면 true. 옆면끼리 잇는 곡선으로 그린다. */
  backward: boolean
  relation: T
}

export interface DiagramLayout<T extends ClassRelation = ClassRelation> {
  boxes: Map<string, PlacedBox>
  paths: Array<RelationPath<T>>
  size: { width: number; height: number }
}

export interface TreeLayoutOptions<T extends ClassRelation = ClassRelation> {
  direction?: TreeDirection
  /** 선의 끝점을 박스 안 특정 줄에 붙이고 싶을 때 쓴다(전체 흐름도의 메서드 줄). */
  anchor?: (relation: T, box: PlacedBox, side: 'from' | 'to') => Point
  /** 층을 직접 정하고 싶을 때 쓴다(진입점 흐름은 메서드의 최소 깊이를 층으로 삼는다). */
  fixedLayers?: ReadonlyMap<string, number>
  gapMain?: number
  gapCross?: number
}

/** 층을 건너뛰는 선이 다른 박스를 가로지르지 않도록 중간 층마다 끼워 넣는 경유점의 크기. */
const dummySize = 22
const defaultGaps = {
  LR: { main: 74, cross: 20 },
  TB: { main: 58, cross: 30 },
}

interface LayeredNode {
  key: string
  layer: number
  w: number
  h: number
  dummy: boolean
  /** 교차축(LR이면 y, TB면 x) 위치. 좌표 배치가 끝나면 x/y로 옮긴다. */
  cross: number
  x: number
  y: number
}

/** 순서·좌표 계산은 관계의 내용이 필요 없어서 이 최소 형태만 본다. */
interface ChainShape {
  keys: string[]
  forward: boolean
}

interface Chain<T extends ClassRelation = ClassRelation> extends ChainShape {
  relation: T
  /** 상속·구현이라 층 방향을 뒤집어 잡은 선. 화살촉은 그대로 상위 타입 쪽에 붙인다. */
  flipped: boolean
}

/**
 * 최장 경로 기준으로 층을 매긴다. 순환은 위상 정렬(Kahn)로 처리하고,
 * 정렬에서 남은 노드는 뒤에 붙여 임의 층을 준다(그 노드로 들어가는 되돌아가는 선은 따로 그린다).
 */
export function assignLayers(keys: string[], edges: Array<[string, string]>): Map<string, number> {
  const indegree = new Map(keys.map((key) => [key, 0]))
  const adjacency = new Map(keys.map((key) => [key, [] as string[]]))
  for (const [from, to] of edges) {
    if (from === to || !adjacency.has(from) || !indegree.has(to)) continue
    adjacency.get(from)!.push(to)
    indegree.set(to, indegree.get(to)! + 1)
  }

  const queue = keys.filter((key) => indegree.get(key) === 0)
  const order: string[] = []
  const ordered = new Set<string>()
  while (queue.length > 0) {
    const key = queue.shift()!
    order.push(key)
    ordered.add(key)
    for (const next of adjacency.get(key)!) {
      indegree.set(next, indegree.get(next)! - 1)
      if (indegree.get(next) === 0) queue.push(next)
    }
  }

  for (const key of keys) {
    if (!ordered.has(key)) order.push(key)
  }

  const layers = new Map(keys.map((key) => [key, 0]))
  for (const key of order) {
    for (const next of adjacency.get(key)!) {
      layers.set(next, Math.max(layers.get(next)!, layers.get(key)! + 1))
    }
  }

  return layers
}

/**
 * 층을 나누고, 층을 2개 이상 건너뛰는 선에 더미를 끼워 넣는다.
 * 관계가 하나도 없는 클래스는 마지막 층에 모아 둔다(층 0에 몰리면 첫 줄이 지나치게 길어진다).
 */
export function buildLayers<T extends ClassRelation>(
  nodes: LayoutNode[],
  relations: T[],
  direction: TreeDirection,
  fixedLayers?: ReadonlyMap<string, number>,
): { rows: LayeredNode[][]; chains: Array<Chain<T>> } {
  const isVertical = direction === 'TB'
  const sizeOf = new Map(nodes.map((node) => [node.id, node]))
  // 상속·구현은 방향을 뒤집어서 층을 잡는다. 상위 타입이 위(또는 왼쪽)에 오게 하기 위해서다.
  const directed = relations.map((relation) => relation.type === 'inherit' || relation.type === 'implement'
    ? { relation, from: relation.to, to: relation.from, flipped: true }
    : { relation, from: relation.from, to: relation.to, flipped: false })

  const linked = new Set<string>()
  for (const edge of directed) {
    linked.add(edge.from)
    linked.add(edge.to)
  }

  const connected = nodes.map((node) => node.id).filter((id) => fixedLayers ? true : linked.has(id))
  const isolated = fixedLayers ? [] : nodes.map((node) => node.id).filter((id) => !linked.has(id))
  const layerOf = fixedLayers
    ? new Map(connected.map((id) => [id, fixedLayers.get(id) ?? 0]))
    : assignLayers(connected, directed.map((edge) => [edge.from, edge.to]))
  const depth = connected.length > 0 ? Math.max(...layerOf.values()) : -1
  const rowCount = depth + (isolated.length > 0 ? 2 : 1)
  const rows: LayeredNode[][] = Array.from({ length: Math.max(rowCount, 0) }, () => [])

  const toLayered = (key: string, layer: number): LayeredNode => {
    const size = sizeOf.get(key)!
    return { key, layer, w: size.w, h: size.h, dummy: false, cross: 0, x: 0, y: 0 }
  }

  for (const id of connected) rows[layerOf.get(id)!].push(toLayered(id, layerOf.get(id)!))
  for (const id of isolated) rows[depth + 1].push(toLayered(id, depth + 1))

  const chains: Array<Chain<T>> = directed.map((edge, index) => {
    const fromLayer = layerOf.get(edge.from)
    const toLayer = layerOf.get(edge.to)
    const keys = [edge.from]
    const forward = fromLayer != null && toLayer != null && toLayer > fromLayer
    if (forward && toLayer! - fromLayer! > 1) {
      for (let layer = fromLayer! + 1; layer < toLayer!; layer += 1) {
        const key = `#dummy-${index}-${layer}`
        rows[layer].push({
          key,
          layer,
          w: isVertical ? dummySize : 2,
          h: isVertical ? 2 : dummySize,
          dummy: true,
          cross: 0,
          x: 0,
          y: 0,
        })
        keys.push(key)
      }
    }

    keys.push(edge.to)
    return { relation: edge.relation, keys, flipped: edge.flipped, forward }
  })

  return { rows, chains }
}

/** 더미까지 포함한 확장 그래프에서 위/아래 이웃 목록을 만든다. */
function buildNeighbors(chains: readonly ChainShape[]): { up: Map<string, string[]>; down: Map<string, string[]> } {
  const up = new Map<string, string[]>()
  const down = new Map<string, string[]>()
  const push = (map: Map<string, string[]>, key: string, value: string) => {
    const list = map.get(key)
    if (list) list.push(value)
    else map.set(key, [value])
  }

  for (const chain of chains) {
    if (!chain.forward) continue
    for (let index = 0; index + 1 < chain.keys.length; index += 1) {
      push(down, chain.keys[index], chain.keys[index + 1])
      push(up, chain.keys[index + 1], chain.keys[index])
    }
  }

  return { up, down }
}

/** 층마다 노드 순서를 바꿔가며 교차를 줄인다(바리센터 정렬을 위/아래로 번갈아 4회). */
export function reduceCrossings(rows: LayeredNode[][], chains: readonly ChainShape[], iterations = 4): void {
  const { up, down } = buildNeighbors(chains)
  const index = new Map<string, number>()
  for (const row of rows) row.forEach((node, position) => index.set(node.key, position))

  const barycenter = (node: LayeredNode, neighbors: Map<string, string[]>) => {
    const list = (neighbors.get(node.key) ?? []).filter((key) => index.has(key))
    if (list.length === 0) return index.get(node.key)!
    return list.reduce((sum, key) => sum + index.get(key)!, 0) / list.length
  }

  for (let iteration = 0; iteration < iterations; iteration += 1) {
    const downward = iteration % 2 === 0
    const sequence = rows.map((_, position) => position)
    const targets = downward ? sequence.slice(1) : sequence.slice(0, -1).reverse()
    for (const layer of targets) {
      const neighbors = downward ? up : down
      rows[layer].sort((left, right) => barycenter(left, neighbors) - barycenter(right, neighbors))
      rows[layer].forEach((node, position) => index.set(node.key, position))
    }
  }
}

/** 인접 층 사이에서 선이 서로 교차하는 횟수. 배치 품질을 테스트로 확인하는 데 쓴다. */
export function countCrossings(rows: LayeredNode[][], chains: readonly ChainShape[]): number {
  const index = new Map<string, number>()
  const layerOf = new Map<string, number>()
  for (const row of rows) {
    row.forEach((node, position) => {
      index.set(node.key, position)
      layerOf.set(node.key, node.layer)
    })
  }

  const segments: Array<{ layer: number; from: number; to: number }> = []
  for (const chain of chains) {
    if (!chain.forward) continue
    for (let position = 0; position + 1 < chain.keys.length; position += 1) {
      const from = index.get(chain.keys[position])
      const to = index.get(chain.keys[position + 1])
      const layer = layerOf.get(chain.keys[position])
      if (from == null || to == null || layer == null) continue
      segments.push({ layer, from, to })
    }
  }

  let crossings = 0
  for (let a = 0; a < segments.length; a += 1) {
    for (let b = a + 1; b < segments.length; b += 1) {
      const left = segments[a]
      const right = segments[b]
      if (left.layer !== right.layer) continue
      if ((left.from - right.from) * (left.to - right.to) < 0) crossings += 1
    }
  }

  return crossings
}

/**
 * 층은 진행축으로 쌓고, 층 안에서는 이웃 중심으로 당긴다.
 * 밀어내기만 하면 전부 한쪽으로 쏠리므로 되당기기까지 해야 부모 아래로 자식이 가운데 정렬된다.
 */
function assignCoordinates(
  rows: LayeredNode[][],
  chains: readonly ChainShape[],
  direction: TreeDirection,
  gapMain: number,
  gapCross: number,
): void {
  const isVertical = direction === 'TB'
  const { up, down } = buildNeighbors(chains)
  const mainSize = (node: LayeredNode) => (isVertical ? node.h : node.w)
  const crossSize = (node: LayeredNode) => (isVertical ? node.w : node.h)

  const layerMain: number[] = []
  let cursor = 0
  for (const row of rows) {
    layerMain.push(cursor)
    cursor += (row.length > 0 ? Math.max(...row.map(mainSize)) : 0) + gapMain
  }

  for (const row of rows) {
    let position = 0
    for (const node of row) {
      node.cross = position
      position += crossSize(node) + gapCross
    }
  }

  const byKey = new Map<string, LayeredNode>()
  for (const row of rows) for (const node of row) byKey.set(node.key, node)
  const centerOf = (key: string) => {
    const node = byKey.get(key)
    return node ? node.cross + crossSize(node) / 2 : null
  }

  for (let iteration = 0; iteration < 6; iteration += 1) {
    const downward = iteration % 2 === 0
    const sequence = rows.map((_, position) => position)
    const targets = downward ? sequence.slice(1) : sequence.slice(0, -1).reverse()
    for (const layer of targets) {
      const row = rows[layer]
      const neighbors = downward ? up : down
      const want = row.map((node) => {
        const centers = (neighbors.get(node.key) ?? []).map(centerOf).filter((value): value is number => value != null)
        if (centers.length === 0) return node.cross
        return centers.reduce((sum, value) => sum + value, 0) / centers.length - crossSize(node) / 2
      })

      let pushed = -Infinity
      row.forEach((node, position) => {
        node.cross = Math.max(want[position], pushed)
        pushed = node.cross + crossSize(node) + gapCross
      })

      let limit = Infinity
      for (let position = row.length - 1; position >= 0; position -= 1) {
        const node = row[position]
        if (want[position] > node.cross) node.cross = Math.min(want[position], limit - crossSize(node))
        limit = node.cross - gapCross
      }
    }
  }

  const all = rows.flat()
  const minCross = all.length > 0 ? Math.min(...all.map((node) => node.cross)) : 0
  for (const node of all) node.cross -= minCross

  for (const node of all) {
    node.x = isVertical ? node.cross : layerMain[node.layer]
    node.y = isVertical ? layerMain[node.layer] : node.cross
  }
}

/**
 * 트리(계층) 배치. 층 나누기 → 더미 삽입 → 바리센터 정렬 → 좌표 배치 순으로 돌린다.
 * 진행 방향 기본값이 `LR`인 이유는 UML 박스가 세로보다 가로로 넓어서, "맞춤" 배율에서 더 크게 보이기 때문이다.
 */
export function layoutTree<T extends ClassRelation>(
  nodes: LayoutNode[],
  relations: T[],
  options: TreeLayoutOptions<T> = {},
): DiagramLayout<T> {
  const direction = options.direction ?? 'LR'
  const boxes = new Map<string, PlacedBox>()
  if (nodes.length === 0) return { boxes, paths: [], size: { width: 0, height: 0 } }

  const isVertical = direction === 'TB'
  const { rows, chains } = buildLayers(nodes, relations, direction, options.fixedLayers)
  reduceCrossings(rows, chains)
  assignCoordinates(
    rows,
    chains,
    direction,
    options.gapMain ?? defaultGaps[direction].main,
    options.gapCross ?? defaultGaps[direction].cross)

  const byKey = new Map<string, LayeredNode>()
  for (const row of rows) for (const node of row) byKey.set(node.key, node)
  for (const node of byKey.values()) {
    if (node.dummy) continue
    boxes.set(node.key, { id: node.key, x: node.x, y: node.y, w: node.w, h: node.h })
  }

  // 끝점 기본값은 진행축 방향의 면 가운데다. 전체 흐름도는 anchor로 메서드 줄에 붙인다.
  const faceAnchor = (box: PlacedBox, face: 'out' | 'in') => isVertical
    ? { x: box.x + box.w / 2, y: face === 'out' ? box.y + box.h : box.y }
    : { x: face === 'out' ? box.x + box.w : box.x, y: box.y + box.h / 2 }

  const paths: Array<RelationPath<T>> = []
  for (const chain of chains) {
    const { relation } = chain
    const from = boxes.get(relation.from)
    const to = boxes.get(relation.to)
    if (!from || !to) continue

    if (!chain.forward) {
      // 되돌아가는 선(순환)은 마주 보는 옆면을 잇는다.
      paths.push({
        type: relation.type,
        from: relation.from,
        to: relation.to,
        d: bezierBetween(from, to),
        backward: true,
        relation,
      })
      continue
    }

    // chain.keys는 낮은 층 → 높은 층 순서다. 상속·구현은 그 순서가 상위 타입 → 하위 타입이라 마지막에 뒤집는다.
    const startIsRelationFrom = chain.keys[0] === relation.from
    const startBox = startIsRelationFrom ? from : to
    const endBox = startIsRelationFrom ? to : from
    const points = [
      options.anchor
        ? options.anchor(relation, startBox, startIsRelationFrom ? 'from' : 'to')
        : faceAnchor(startBox, 'out'),
    ]
    for (const key of chain.keys.slice(1, -1)) {
      const dummy = byKey.get(key)
      if (!dummy) continue
      points.push({ x: dummy.x + dummy.w / 2, y: dummy.y + dummy.h / 2 })
    }

    points.push(options.anchor
      ? options.anchor(relation, endBox, startIsRelationFrom ? 'to' : 'from')
      : faceAnchor(endBox, 'in'))
    // 화살촉(marker-end)이 상위 타입 쪽에 붙도록 그리는 방향을 되돌린다.
    if (chain.flipped) points.reverse()
    paths.push({
      type: relation.type,
      from: relation.from,
      to: relation.to,
      d: smoothPath(points, !isVertical),
      backward: false,
      relation,
    })
  }

  const all = [...byKey.values()]
  return {
    boxes,
    paths,
    size: {
      width: Math.max(0, ...all.map((node) => node.x + node.w)),
      height: Math.max(0, ...all.map((node) => node.y + node.h)),
    },
  }
}

/** 화면에 보이는 영역(문서 좌표). 스크롤·줌으로 바뀐다. */
export interface Viewport {
  left: number
  top: number
  width: number
  height: number
}

/**
 * 뷰포트 밖 박스를 걸러낸다. 클래스가 수백 개인 솔루션에서 DOM 노드 수를 줄이는 유일한 손잡이다.
 * 여백(margin)을 둬서 스크롤 직후에도 빈자리가 보이지 않게 한다.
 */
export function cullToViewport<T extends { id: string }>(
  items: readonly T[],
  boxes: ReadonlyMap<string, { x: number; y: number; w: number; h: number }>,
  viewport: Viewport | null,
  margin = 600,
): readonly T[] {
  if (!viewport || viewport.width <= 0 || viewport.height <= 0) return items
  const left = viewport.left - margin
  const top = viewport.top - margin
  const right = viewport.left + viewport.width + margin
  const bottom = viewport.top + viewport.height + margin
  return items.filter((item) => {
    const box = boxes.get(item.id)
    if (!box) return false
    return box.x < right && box.x + box.w > left && box.y < bottom && box.y + box.h > top
  })
}
