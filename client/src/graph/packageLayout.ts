import { bezierBetween } from './geometry'
import type { ClassRelation } from './relations'
import { assignLayers, type DiagramLayout, type LayoutNode, type PlacedBox, type RelationPath } from './treeLayout'

export interface PlacedPackage {
  namespace: string
  x: number
  y: number
  w: number
  h: number
}

export interface PackageLayout extends DiagramLayout {
  packages: PlacedPackage[]
}

const cellGap = 16
const padX = 12
const padY = 30
const packageGap = 26
const minAvailableWidth = 760

/**
 * 패키지 배치. 네임스페이스별 격자로 묶고, 패키지 사이 의존 방향으로 정렬한 뒤
 * 지금 패널 폭(`availableWidth`)에 맞춰 줄바꿈하며 흘린다.
 */
export function layoutPackages(
  nodes: LayoutNode[],
  namespaceOf: Map<string, string>,
  relations: ClassRelation[],
  availableWidth: number,
): PackageLayout {
  const boxes = new Map<string, PlacedBox>()
  if (nodes.length === 0) return { boxes, packages: [], paths: [], size: { width: 0, height: 0 } }

  const grouped = new Map<string, LayoutNode[]>()
  for (const node of nodes) {
    const namespace = namespaceOf.get(node.id) ?? ''
    const list = grouped.get(namespace)
    if (list) list.push(node)
    else grouped.set(namespace, [node])
  }

  interface PackageDraft extends PlacedPackage {
    items: Array<LayoutNode & { offsetX: number; offsetY: number }>
  }

  const packages: PackageDraft[] = []
  for (const [namespace, items] of grouped) {
    const columns = Math.min(3, Math.max(1, Math.ceil(Math.sqrt(items.length))))
    const cellWidth = Math.max(...items.map((item) => item.w))
    const rowHeights: number[] = []
    items.forEach((item, index) => {
      const row = Math.floor(index / columns)
      rowHeights[row] = Math.max(rowHeights[row] ?? 0, item.h)
    })

    const placed = items.map((item, index) => ({
      ...item,
      offsetX: padX + (index % columns) * (cellWidth + cellGap),
      offsetY: padY + rowHeights
        .slice(0, Math.floor(index / columns))
        .reduce((sum, height) => sum + height + cellGap, 0),
    }))

    packages.push({
      namespace,
      x: 0,
      y: 0,
      w: padX * 2 + columns * cellWidth + (columns - 1) * cellGap,
      h: padY + rowHeights.reduce((sum, height) => sum + height + cellGap, 0) - cellGap + padX,
      items: placed,
    })
  }

  // 네임스페이스에는 못 들어가는 문자라 두 이름을 안전하게 이어 붙일 수 있다.
  const separator = '>'
  const packagePairs = [...new Set(relations
    .filter((relation) => namespaceOf.get(relation.from) !== namespaceOf.get(relation.to))
    .map((relation) => `${namespaceOf.get(relation.from) ?? ''}${separator}${namespaceOf.get(relation.to) ?? ''}`))]
    .map((key) => key.split(separator) as [string, string])
  const layerOf = assignLayers(packages.map((item) => item.namespace), packagePairs)
  packages.sort((left, right) =>
    (layerOf.get(left.namespace) ?? 0) - (layerOf.get(right.namespace) ?? 0) ||
    left.namespace.localeCompare(right.namespace))

  const available = Math.max(minAvailableWidth, availableWidth)
  let cursorX = 0
  let cursorY = 0
  let rowHeight = 0
  let width = 0
  for (const item of packages) {
    if (cursorX > 0 && cursorX + item.w > available) {
      cursorX = 0
      cursorY += rowHeight + packageGap
      rowHeight = 0
    }

    item.x = cursorX
    item.y = cursorY
    cursorX += item.w + packageGap
    rowHeight = Math.max(rowHeight, item.h)
    width = Math.max(width, item.x + item.w)
  }

  for (const item of packages) {
    for (const child of item.items) {
      boxes.set(child.id, {
        id: child.id,
        x: item.x + child.offsetX,
        y: item.y + child.offsetY,
        w: child.w,
        h: child.h,
      })
    }
  }

  const paths: RelationPath[] = []
  for (const relation of relations) {
    const from = boxes.get(relation.from)
    const to = boxes.get(relation.to)
    if (!from || !to) continue
    paths.push({ type: relation.type, from: relation.from, to: relation.to, d: bezierBetween(from, to), backward: false, relation })
  }

  return {
    boxes,
    packages: packages.map(({ items: _items, ...rest }) => rest),
    paths,
    size: { width, height: cursorY + rowHeight },
  }
}
