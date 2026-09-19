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
    classes: graphWindow.branchNodeIds.has(node.id) ? 'branch' : '',
  }))

  // 접힌 팬아웃은 요약 노드 하나로 대신한다. 누르면 그 종류만 펼친다.
  const groupNodes = graphWindow.groups.map((group) => ({
    data: {
      id: group.id,
      label: groupLabel(group.kind, group.nodeIds.length),
      color: kindColors[group.kind],
      badge: 'none',
    },
    classes: 'fanout',
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

export function buildStyle(colors: StyleColors): cytoscape.StylesheetStyle[] {
  return [
    {
      selector: 'node',
      style: {
        // 왼쪽 6%를 종류 색으로 칠한다. 상자 전체를 칠하면 글자 대비가 무너진다.
        'background-fill': 'linear-gradient',
        'background-gradient-direction': 'to-right',
        'background-gradient-stop-colors':
          `data(color) data(color) ${colors.surface} ${colors.surface}` as unknown as string[],
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
  ]
}
