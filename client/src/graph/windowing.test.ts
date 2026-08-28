import { describe, expect, it } from 'vitest'
import { getClassRelationWindow, getDrilldownWindow, getInitialWindow } from './windowing'
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
  schemaVersion: 3,
  solutionPath: 'test.sln',
  rootDirectory: '.',
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
    graphClass('Tests.Constants', 'Constants', []),
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
  it('Level 1은 직선 체인과 첫 분기 지점의 직접 호출 대상까지 반환한다', () => {
    const result = getInitialWindow(graph, 'A')

    expect(result.nodes.map((item) => item.id)).toEqual(['A', 'B', 'C', 'D', 'E'])
    // 펼쳐진 분기 노드에는 표시가 남지 않고, 아직 못 펼친 하위 호출이 있는 노드에만 붙는다.
    expect(result.branchNodeIds.has('C')).toBe(false)
    expect(result.branchNodeIds.has('D')).toBe(true)
    expect(result.kind).toBe('callFlow')
  })

  it('진입점이 바로 여러 곳을 호출해도 그 호출 대상을 보여준다', () => {
    const result = getInitialWindow(graph, 'C')

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E'])
    expect(result.edges).toHaveLength(2)
  })

  it('호출 대상이 없는 진입점은 자기 노드만 반환한다', () => {
    const result = getInitialWindow(graph, 'F')

    expect(result.nodes.map((item) => item.id)).toEqual(['F'])
    expect(result.branchNodeIds.size).toBe(0)
  })

  it('드릴다운은 포커스에서 지정한 홉까지 모든 하위 호출을 반환한다', () => {
    const result = getDrilldownWindow(graph, 'C', 2)

    expect(result.nodes.map((item) => item.id)).toEqual(['C', 'D', 'E', 'F'])
    expect(result.edges).toHaveLength(3)
  })
})

describe('클래스 관계도 윈도잉', () => {
  it('고른 클래스와 직접 관계된 클래스만 클래스 단위로 보여준다', () => {
    const result = getClassRelationWindow(classGraph, 'Tests.Target')

    expect(result.kind).toBe('classRelations')
    expect(result.focusNodeId).toBe('Tests.Target')
    expect(result.nodes.map((item) => item.id).sort()).toEqual(['Tests.Callee', 'Tests.Caller', 'Tests.Target'])
    expect(result.edges).toEqual([
      { from: 'Tests.Caller', to: 'Tests.Target' },
      { from: 'Tests.Target', to: 'Tests.Callee' },
    ])
  })

  it('클래스 노드는 그 클래스 선언 위치를 가리킨다', () => {
    const result = getClassRelationWindow(classGraph, 'Tests.Target')
    const focus = result.nodes.find((item) => item.id === 'Tests.Target')

    expect(focus).toMatchObject({ displayName: 'Target', file: 'Target.cs', startLine: 1, endLine: 9 })
  })

  it('아직 안 보이는 관계가 남은 이웃 클래스에만 펼침 표시를 붙인다', () => {
    const result = getClassRelationWindow(classGraph, 'Tests.Caller')

    expect(result.nodes.map((item) => item.id)).toEqual(['Tests.Caller', 'Tests.Target'])
    expect(result.branchNodeIds.has('Tests.Target')).toBe(true)
    expect(result.branchNodeIds.has('Tests.Caller')).toBe(false)
  })

  it('메서드가 없어 호출 관계가 없는 클래스도 자기 노드는 보여준다', () => {
    const result = getClassRelationWindow(classGraph, 'Tests.Constants')

    expect(result.nodes.map((item) => item.id)).toEqual(['Tests.Constants'])
    expect(result.edges).toHaveLength(0)
  })

  it('목록에 없는 클래스는 빈 창을 반환한다', () => {
    const result = getClassRelationWindow(classGraph, 'Tests.Missing')

    expect(result.nodes).toHaveLength(0)
    expect(result.edges).toHaveLength(0)
  })
})
