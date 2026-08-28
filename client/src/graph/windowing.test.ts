import { describe, expect, it } from 'vitest'
import { getClassWindow, getDrilldownWindow, getInitialWindow } from './windowing'
import type { GraphClass, GraphDocument, GraphNode } from '../types/graph'

const node = (id: string, classId = 'Tests.Sample'): GraphNode => ({
  id,
  kind: 'Method',
  displayName: id,
  namespace: 'Tests',
  classId,
  file: `${id}.cs`,
  startLine: 1,
  endLine: 2,
})

const graphClass = (id: string, name: string, methodIds: string[]): GraphClass => ({
  id,
  name,
  namespace: 'Tests',
  kind: 'Method',
  file: `${name}.cs`,
  startLine: 1,
  endLine: 9,
  methodIds,
})

const graph: GraphDocument = {
  schemaVersion: 2,
  solutionPath: 'test.sln',
  analyzedAt: '2026-01-01T00:00:00Z',
  entryPoints: [{ nodeId: 'A', httpMethod: 'GET', route: '/test' }],
  classes: [
    graphClass('Tests.Sample', 'Sample', ['A', 'B', 'C', 'D', 'E', 'F']),
  ],
  nodes: ['A', 'B', 'C', 'D', 'E', 'F'].map((id) => node(id)),
  edges: [
    { from: 'A', to: 'B' },
    { from: 'B', to: 'C' },
    { from: 'C', to: 'D' },
    { from: 'C', to: 'E' },
    { from: 'D', to: 'F' },
  ],
  fileHashes: {},
}

const classGraph: GraphDocument = {
  ...graph,
  classes: [
    graphClass('Tests.Caller', 'Caller', ['Caller.Run']),
    graphClass('Tests.Target', 'Target', ['Target.First', 'Target.Second', 'Target.Unused']),
    graphClass('Tests.Callee', 'Callee', ['Callee.Save']),
  ],
  nodes: [
    node('Caller.Run', 'Tests.Caller'),
    node('Target.First', 'Tests.Target'),
    node('Target.Second', 'Tests.Target'),
    node('Target.Unused', 'Tests.Target'),
    node('Callee.Save', 'Tests.Callee'),
    node('Other.Ignored', 'Tests.Other'),
  ],
  edges: [
    { from: 'Caller.Run', to: 'Target.First' },
    { from: 'Target.First', to: 'Target.Second' },
    { from: 'Target.Second', to: 'Callee.Save' },
    { from: 'Other.Ignored', to: 'Other.Ignored' },
  ],
}

describe('포커스 기반 윈도잉', () => {
  it('Level 1은 첫 분기 노드까지 직선 체인만 반환한다', () => {
    const result = getInitialWindow(graph, 'A')

    expect(result.nodes.map((item) => item.id)).toEqual(['A', 'B', 'C'])
    expect(result.branchNodeIds.has('C')).toBe(true)
    expect(result.groupClassId).toBeNull()
  })

  it('드릴다운은 포커스에서 지정한 홉까지 모든 하위 호출을 반환한다', () => {
    const result = getDrilldownWindow(graph, 'C', 2)

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E', 'F'])
    expect(result.edges).toHaveLength(3)
  })
})

describe('클래스 중심 윈도잉', () => {
  it('클래스의 모든 메서드와 직접 호출자/피호출자를 반환한다', () => {
    const result = getClassWindow(classGraph, 'Tests.Target')

    expect(result.nodes.map((item) => item.id).sort()).toEqual([
      'Callee.Save',
      'Caller.Run',
      'Target.First',
      'Target.Second',
      'Target.Unused',
    ])
    expect(result.edges).toHaveLength(3)
    expect(result.groupClassId).toBe('Tests.Target')
    expect(result.groupLabel).toBe('Target')
    expect(result.focusNodeId).toBe('Tests.Target')
  })

  it('호출 관계가 없는 클래스는 자기 메서드만 반환한다', () => {
    const result = getClassWindow(classGraph, 'Tests.Callee')

    expect(result.nodes.map((item) => item.id).sort()).toEqual(['Callee.Save', 'Target.Second'])
  })

  it('그래프에 메서드가 없는 클래스는 빈 창을 반환한다', () => {
    const result = getClassWindow(classGraph, 'Tests.Missing')

    expect(result.nodes).toHaveLength(0)
    expect(result.edges).toHaveLength(0)
    expect(result.groupLabel).toBe('Tests.Missing')
  })
})
