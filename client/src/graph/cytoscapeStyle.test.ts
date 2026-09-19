import cytoscape from 'cytoscape'
import { describe, expect, it } from 'vitest'
import { buildElements, buildStyle, nodeLabel, toCytoscapeColor } from './cytoscapeStyle'
import { kindColors } from '../components/kindColors'
import type { GraphWindow, NodeKind } from '../types/graph'

function node(id: string, kind: NodeKind) {
  return {
    id,
    kind,
    displayName: id,
    namespace: 'N',
    className: 'C',
    file: 'C.cs',
    startLine: 1,
    endLine: 2,
  }
}

const graphWindow: GraphWindow = {
  nodes: [node('a', 'Controller'), node('b', 'Service')],
  edges: [{ from: 'a', to: 'b', confidence: 'Estimated' }],
  branchNodeIds: new Set(['a']),
  cycleNodeIds: new Set<string>(),
  groups: [{ id: 'fanout:b:Repository', kind: 'Repository', parentNodeId: 'b', nodeIds: ['c', 'd'] }],
  direction: 'down',
  truncated: false,
  hiddenCount: 0,
} as unknown as GraphWindow

describe('cytoscapeStyle', () => {
  /**
   * 스타일시트가 파싱되지 않으면 예외가 캔버스가 아니라 앱 전체를 죽인다.
   * 실제로 `background-gradient-stop-colors: data(color)`가 그랬다 —
   * 진입점을 고르는 순간 화면이 빈 채로 멈췄다. 여기서 실제 Cytoscape로 파싱해 본다.
   */
  it('실제 Cytoscape가 스타일과 요소를 받아들인다', () => {
    const elements = buildElements(graphWindow, kindColors, new Map(), (kind, count) => `${kind} ${count}`)
    const style = buildStyle(
      {
        label: '#ffffff',
        // 번들러가 CSS의 rgba를 8자리 hex로 줄여 놓는다. 그 값이 그대로 들어와도 버티는지 본다.
        nodeBorder: '#ffffff30',
        surface: '#1b2536',
        edge: '#657086',
        accent: '#8ddcff',
      },
      kindColors,
    )

    // Cytoscape는 못 읽는 속성을 조용히 무시하고 경고만 남긴다(테두리 색이 사라져도 화면은 뜬다).
    const warnings: string[] = []
    const originalWarn = console.warn
    console.warn = (...args: unknown[]) => void warnings.push(args.join(' '))

    let instance: cytoscape.Core | null = null
    try {
      instance = cytoscape({ headless: true, styleEnabled: true, elements, style })
      // 종류 색 띠가 리터럴 색으로 적용됐는지 본다(`data()`는 여기서 쓸 수 없다).
      expect(instance.$id('a').style('background-gradient-stop-colors')).toContain('rgb(124,109,242)')
      expect(warnings.filter((line) => line.includes('invalid'))).toEqual([])
    } finally {
      console.warn = originalWarn
      instance?.destroy()
    }
  })

  it('알파가 붙은 hex를 rgba로 바꾼다', () => {
    expect(toCytoscapeColor('#ffffff30')).toBe('rgba(255, 255, 255, 0.19)')
    expect(toCytoscapeColor('#0af8')).toBe('rgba(0, 170, 255, 0.53)')
    // 알파가 없는 값과 이미 함수 형태인 값은 그대로 둔다.
    expect(toCytoscapeColor('#1b2536')).toBe('#1b2536')
    expect(toCytoscapeColor('rgb(1, 2, 3)')).toBe('rgb(1, 2, 3)')
  })

  it('노드 라벨은 클래스와 메서드를 두 줄로 나눈다', () => {
    expect(nodeLabel('Orders.OrdersController.Create', { httpMethod: 'POST' }))
      .toBe(`POST OrdersController${String.fromCharCode(10)}Create`)
    expect(nodeLabel('Run', { cycle: true })).toBe('Run ↺')
  })
})
