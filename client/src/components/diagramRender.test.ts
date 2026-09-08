import { createElement, createRef } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { buildClassDiagram, type ClassDiagramOptions } from '../graph/classDiagram'
import { buildEntryFlow, buildEntryMap } from '../graph/flowMap'
import { collapseEdgesToClasses, deriveRelations } from '../graph/relations'
import type { GraphClass, GraphDocument, GraphNode } from '../types/graph'
import { entryPoint, graphDefaults, nodeDefaults } from '../types/testFactories'
import { ClassDiagram } from './ClassDiagram'
import { FlowDiagram } from './FlowDiagram'

const node = (id: string, classId: string): GraphNode => ({
  id,
  kind: 'Method',
  displayName: id,
  namespace: 'App',
  classId,
  file: `${classId}.cs`,
  startLine: 1,
  endLine: 5,
  accessibility: 'Public',
  returnType: 'Task',
  parameterTypes: [],
  ...nodeDefaults,
})

const graphClass = (id: string, overrides: Partial<GraphClass> = {}): GraphClass => ({
  id,
  name: id.split('.').pop() ?? id,
  namespace: id.split('.').slice(0, -1).join('.'),
  kind: 'Method',
  typeKind: 'Class',
  isAbstract: false,
  isStatic: false,
  baseTypeId: null,
  baseTypeName: null,
  interfaceIds: [],
  interfaceNames: [],
  file: `${id}.cs`,
  startLine: 1,
  endLine: 30,
  methodIds: [],
  members: [],
  ...overrides,
})

const graph: GraphDocument = {
  schemaVersion: 4,
  solutionPath: 'App.sln',
  rootDirectory: '.',
  analyzedAt: '2026-01-01T00:00:00Z',
  entryPoints: [entryPoint('App.Api.OrdersController.Create', 'POST', '/api/orders')],
  classes: [
    graphClass('App.Api.OrdersController', {
      kind: 'Controller',
      baseTypeName: 'ControllerBase',
      methodIds: ['App.Api.OrdersController.Create'],
      members: [
        {
          memberKind: 'Field', name: 'service', signature: null, type: 'IOrderService',
          typeIds: ['App.Core.IOrderService'], accessibility: 'Private', isStatic: false,
          startLine: 3, endLine: 3, nodeId: null,
        },
        {
          memberKind: 'Method', name: 'Create', signature: 'Create(int)', type: 'Task',
          typeIds: [], accessibility: 'Public', isStatic: false,
          startLine: 5, endLine: 12, nodeId: 'App.Api.OrdersController.Create',
        },
      ],
    }),
    graphClass('App.Core.IOrderService', { typeKind: 'Interface', kind: 'Interface' }),
    graphClass('App.Core.OrderService', {
      kind: 'Service',
      interfaceIds: ['App.Core.IOrderService'],
      methodIds: ['App.Core.OrderService.CreateAsync'],
    }),
    graphClass('App.Core.Lonely'),
  ],
  nodes: [
    node('App.Api.OrdersController.Create', 'App.Api.OrdersController'),
    node('App.Core.OrderService.CreateAsync', 'App.Core.OrderService'),
  ],
  edges: [{ from: 'App.Api.OrdersController.Create', to: 'App.Core.OrderService.CreateAsync' }],
  fileHashes: {},
  ...graphDefaults,
  startupFlow: null,
  dataAccess: null,
  middleware: null,
}

const relations = deriveRelations(graph.classes, collapseEdgesToClasses(graph))

const options: ClassDiagramOptions = {
  layout: 'tree',
  treeDirection: 'LR',
  showFields: true,
  showMethods: true,
  relations: { inherit: true, implement: true, assoc: true, depend: true },
  hiddenNamespaces: new Set(),
  focusOnly: false,
  selection: null,
  availableWidth: 1200,
  namespacePrefix: 'App.',
}

const renderClassDiagram = (overrides: Partial<ClassDiagramOptions> = {}, selectedClassId: string | null = null) => {
  const merged = { ...options, ...overrides }
  const view = buildClassDiagram(graph.classes, relations, merged, (id) =>
    graph.nodes.find((item) => item.id === id)?.classId)
  return renderToStaticMarkup(createElement(ClassDiagram, {
    view,
    options: merged,
    selection: selectedClassId ? { type: 'class', key: selectedClassId } : null,
    selectedClassId,
    selectedMemberKey: null,
    zoom: 0.75,
    shellRef: createRef<HTMLDivElement>(),
    scrollTarget: null,
    notice: null,
    onSelectClass: () => undefined,
    onSelectMember: () => undefined,
  }))
}

describe('클래스 다이어그램 렌더', () => {
  it('UML 박스와 관계 선을 그린다', () => {
    const markup = renderClassDiagram()

    expect(markup).toContain('OrdersController')
    expect(markup).toContain('«interface»')
    expect(markup).toContain('rel-implement')
    expect(markup).toContain('service')
    // 멤버 줄과 캡션이 함께 나와야 한다.
    expect(markup).toContain('Create(int)')
  })

  it('멤버를 끄면 멤버 줄이 사라진다', () => {
    const markup = renderClassDiagram({ showFields: false, showMethods: false })

    expect(markup).toContain('OrdersController')
    expect(markup).not.toContain('Create(int)')
  })

  it('선택한 클래스와 관계없는 박스는 흐리게 그린다', () => {
    const markup = renderClassDiagram({}, 'App.Core.IOrderService')

    expect(markup).toContain('uml dim')
    expect(markup).toContain('rel rel-implement active')
  })

  it('패키지 배치에서는 네임스페이스 박스를 그린다', () => {
    const markup = renderClassDiagram({ layout: 'package' })

    expect(markup).toContain('pkg-title')
    expect(markup).toContain('Api')
  })
})

describe('전체 흐름도 렌더', () => {
  const renderFlow = (view: ReturnType<typeof buildEntryMap>) => renderToStaticMarkup(createElement(FlowDiagram, {
    view,
    selection: null,
    zoom: 0.75,
    shellRef: createRef<HTMLDivElement>(),
    onSelectClass: () => undefined,
    onSelectRow: () => undefined,
  }))

  it('진입점 지도는 컨트롤러 박스에 라우트를 줄로 붙인다', () => {
    const markup = renderFlow(buildEntryMap(graph, graph.entryPoints, 'App.'))

    expect(markup).toContain('/api/orders')
    expect(markup).toContain('http post')
    expect(markup).toContain('dg-node')
  })

  it('진입점 흐름은 도달한 메서드를 클래스로 묶는다', () => {
    const view = buildEntryFlow(graph, graph.entryPoints[0].nodeId, 3, graph.entryPoints, 'App.')

    expect(view.boxes.map((box) => box.id)).toEqual(['App.Api.OrdersController', 'App.Core.OrderService'])
    expect(view.fixedLayers?.get('App.Core.OrderService')).toBe(1)
    expect(renderFlow(view)).toContain('dg-row')
  })

  it('깊이 제한에 걸린 호출은 +n으로 남는다', () => {
    const view = buildEntryFlow(graph, graph.entryPoints[0].nodeId, 0, graph.entryPoints, 'App.')

    expect(view.hiddenCount).toBe(1)
    expect(view.boxes[0].rows[0].badge).toBe('+1')
  })
})
