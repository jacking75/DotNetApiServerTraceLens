import type { EntryPoint, GraphWindow, NodeKind } from '../types/graph'

/**
 * 호출 흐름 캔버스(Cytoscape)의 요소·스타일을 만든다.
 * React에 의존하지 않는 순수 함수로 두어 노드 라벨 규칙을 테스트할 수 있게 한다.
 */

/** 노드 상자 크기. 두 줄(클래스 / 메서드)이 들어가야 하므로 예전보다 높다. */
export const nodeWidth = 216
export const nodeHeight = 56

/** 펼침 배지(`⊕`)의 지름. 라벨 위 작은 원은 누르기 어려웠다. */
export const expandBadgeSize = 24

/** 노드 라벨. 첫 줄은 동사·클래스, 둘째 줄은 메서드 이름이다. */
export function nodeLabel(
  displayName: string,
  options: { httpMethod?: string; cycle?: boolean } = {},
): string {
  const index = displayName.lastIndexOf('.')
  const owner = index > 0 ? displayName.slice(0, index) : ''
  const member = index > 0 ? displayName.slice(index + 1) : displayName
  const shortOwner = owner.split('.').pop() ?? owner
  const head = [options.httpMethod, shortOwner].filter(Boolean).join(' ')
  const tail = options.cycle ? `${member} ↺` : member
  return head.length > 0 ? `${head}\n${tail}` : tail
}

/** 펼침 배지 이미지. 데이터 URI라 네트워크 요청이 없다. */
function expandBadge(color: string): string {
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">` +
    `<circle cx="12" cy="12" r="11" fill="${color}" stroke="#ffffff" stroke-width="1.5"/>` +
    `<path d="M12 6 V18 M6 12 H18" stroke="#ffffff" stroke-width="2.4" stroke-linecap="round"/>` +
    `</svg>`
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`
}

/**
 * Cytoscape가 읽을 수 있는 색으로 바꾼다.
 *
 * CSS 변수에서 읽은 값이 `#rrggbbaa`(알파 포함 8자리)면 Cytoscape가 **색으로 읽지 못하고**
 * 그 속성을 통째로 버린다(경고만 남고 테두리가 사라진다). 소스에 `rgba()`로 적어도
 * 번들러가 8자리 hex로 줄여 놓기 때문에 읽는 쪽에서 되돌린다.
 */
export function toCytoscapeColor(value: string): string {
  const text = value.trim()
  const match = /^#([0-9a-f]{3,8})$/i.exec(text)
  if (!match) return text

  const digits = match[1]
  if (digits.length === 4 || digits.length === 8) {
    const size = digits.length / 4
    const part = (index: number) => {
      const raw = digits.slice(index * size, index * size + size)
      return parseInt(size === 1 ? raw + raw : raw, 16)
    }

    const alpha = Math.round((part(3) / 255) * 100) / 100
    return `rgba(${part(0)}, ${part(1)}, ${part(2)}, ${alpha})`
  }

  return text
}

export interface StyleColors {
  label: string
  nodeBorder: string
  surface: string
  edge: string
  accent: string
}

export function buildElements(
  graphWindow: GraphWindow,
  kindColors: Record<NodeKind, string>,
  entryPointByNode: Map<string, EntryPoint>,
  groupLabel: (kind: NodeKind, count: number) => string,
): cytoscape.ElementDefinition[] {
  const nodes = graphWindow.nodes.map((node) => ({
    data: {
      id: node.id,
      label: nodeLabel(node.displayName, {
        httpMethod: entryPointByNode.get(node.id)?.httpMethod,
        cycle: graphWindow.cycleNodeIds.has(node.id),
      }),
      color: kindColors[node.kind],
      badge: graphWindow.branchNodeIds.has(node.id) ? expandBadge(kindColors[node.kind]) : 'none',
    },
    // 종류 색은 클래스로 넘긴다 — 여러 값을 받는 속성 안에서는 `data()`를 쓸 수 없다(아래 buildStyle 주석).
    classes: [`kind-${node.kind}`, graphWindow.branchNodeIds.has(node.id) ? 'branch' : '']
      .filter(Boolean)
      .join(' '),
  }))

  // 접힌 팬아웃은 요약 노드 하나로 대신한다. 누르면 그 종류만 펼친다.
  const groupNodes = graphWindow.groups.map((group) => ({
    data: {
      id: group.id,
      label: groupLabel(group.kind, group.nodeIds.length),
      color: kindColors[group.kind],
      badge: 'none',
    },
    classes: `fanout kind-${group.kind}`,
  }))

  const groupEdges = graphWindow.groups.map((group) => ({
    data: {
      id: `edge-group-${group.id}`,
      source: graphWindow.direction === 'up' ? group.id : group.parentNodeId,
      target: graphWindow.direction === 'up' ? group.parentNodeId : group.id,
    },
    classes: 'fanout',
  }))

  const edges = graphWindow.edges.map((edge, index) => ({
    data: {
      id: `edge-${index}-${edge.from}-${edge.to}`,
      // 호출자 창은 위로 자란다. 화살표는 언제나 "부르는 쪽 → 불리는 쪽"을 가리켜야 한다.
      source: edge.from,
      target: edge.to,
    },
    classes: edge.confidence === 'Estimated' ? 'estimated' : '',
  }))

  return [...nodes, ...groupNodes, ...edges, ...groupEdges]
}

/**
 * 종류별 색 띠. 노드마다 색이 다르지만 `data(color)`를 쓸 수 없다 —
 * Cytoscape는 값을 여러 개 받는 속성(`background-gradient-stop-colors`)을 공백으로 쪼개
 * 하나씩 파싱하는데, 그 안의 `data(...)`는 색으로 읽히지 않아 **스타일시트 파싱이 예외를 던진다**
 * (`Cannot read properties of null (reading 'value')`). 그러면 호출 흐름 캔버스가 통째로 죽는다.
 * 그래서 노드에 `kind-<종류>` 클래스를 주고 종류마다 리터럴 색 규칙을 하나씩 만든다.
 */
function kindBandStyles(
  colors: StyleColors,
  kindColors: Record<NodeKind, string>,
): cytoscape.StylesheetStyle[] {
  return (Object.keys(kindColors) as NodeKind[]).map((kind) => ({
    selector: `node.kind-${kind}`,
    style: {
      'background-gradient-stop-colors':
        `${kindColors[kind]} ${kindColors[kind]} ${colors.surface} ${colors.surface}` as unknown as string[],
    },
  }))
}

export function buildStyle(
  input: StyleColors,
  kindColors: Record<NodeKind, string>,
): cytoscape.StylesheetStyle[] {
  const colors: StyleColors = {
    label: toCytoscapeColor(input.label),
    nodeBorder: toCytoscapeColor(input.nodeBorder),
    surface: toCytoscapeColor(input.surface),
    edge: toCytoscapeColor(input.edge),
    accent: toCytoscapeColor(input.accent),
  }

  return [
    {
      selector: 'node',
      style: {
        // 왼쪽 6%를 종류 색으로 칠한다. 상자 전체를 칠하면 글자 대비가 무너진다.
        'background-fill': 'linear-gradient',
        'background-gradient-direction': 'to-right',
        // 종류 규칙이 덮어쓴다. 정지점 개수를 맞춰 두어야 색과 위치가 어긋나지 않는다.
        'background-gradient-stop-colors':
          `${colors.surface} ${colors.surface} ${colors.surface} ${colors.surface}` as unknown as string[],
        'background-gradient-stop-positions': '0% 6% 6% 100%' as unknown as string[],
        'background-image': 'data(badge)',
        'background-image-containment': 'over',
        'background-width': `${expandBadgeSize}px`,
        'background-height': `${expandBadgeSize}px`,
        'background-position-x': '100%',
        'background-position-y': '100%',
        'background-offset-x': -4,
        'background-offset-y': -4,
        'background-clip': 'none',
        color: colors.label,
        label: 'data(label)',
        'font-size': 12,
        'font-weight': 600,
        'text-wrap': 'wrap',
        'text-max-width': `${nodeWidth - 40}px`,
        // 라벨을 상자 안에 그린다. 기본값(top)이면 이름이 상자 위에 뜬다.
        'text-valign': 'center',
        'text-halign': 'center',
        'text-margin-x': 6,
        width: nodeWidth,
        height: nodeHeight,
        shape: 'round-rectangle',
        'border-width': 1,
        'border-color': colors.nodeBorder,
      },
    },
    {
      selector: 'node.fanout',
      style: { 'border-style': 'dashed', 'border-width': 2, height: 40 },
    },
    {
      selector: 'node.selected',
      style: { 'border-width': 3, 'border-color': colors.accent },
    },
    {
      selector: 'edge',
      style: {
        width: 2,
        'line-color': colors.edge,
        'target-arrow-color': colors.edge,
        'target-arrow-shape': 'triangle',
        'arrow-scale': 1.6,
        'curve-style': 'bezier',
      },
    },
    {
      // 람다 본문 안의 호출만 있는 엣지는 추정이다. 실선과 구분해서 그린다.
      selector: 'edge.estimated',
      style: { 'line-style': 'dashed' },
    },
    {
      selector: 'edge.fanout',
      style: { 'line-style': 'dotted' },
    },
    {
      selector: 'edge.on-path',
      style: { width: 4, 'line-color': colors.accent, 'target-arrow-color': colors.accent },
    },
    {
      selector: 'node.on-path',
      style: { 'border-width': 2, 'border-color': colors.accent },
    },
    // 종류 규칙은 기본 노드 규칙 뒤에 와야 색 띠가 이긴다.
    ...kindBandStyles(colors, kindColors),
  ]
}
