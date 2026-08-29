import { describe, expect, it } from 'vitest'
import { deriveRelations } from './relations'
import type { GraphClass, GraphClassMember } from '../types/graph'

const member = (name: string, typeIds: string[], memberKind: GraphClassMember['memberKind'] = 'Field'): GraphClassMember => ({
  memberKind,
  name,
  signature: memberKind === 'Method' ? `${name}()` : null,
  type: typeIds[0] ?? 'int',
  typeIds,
  accessibility: 'Private',
  isStatic: false,
  startLine: 1,
  endLine: 1,
  nodeId: null,
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
  endLine: 9,
  methodIds: [],
  members: [],
  ...overrides,
})

const classes: GraphClass[] = [
  graphClass('App.IOrderRepository', { typeKind: 'Interface' }),
  graphClass('App.OrderRepository', { interfaceIds: ['App.IOrderRepository'] }),
  graphClass('App.EntityBase', { isAbstract: true }),
  graphClass('App.Order', { baseTypeId: 'App.EntityBase' }),
  graphClass('App.OrderService', {
    members: [
      member('repository', ['App.IOrderRepository']),
      member('Find', ['App.Order'], 'Method'),
    ],
  }),
  graphClass('App.Reporter'),
]

describe('관계 파생', () => {
  it('상속·구현·연관을 선언에서 만든다', () => {
    const relations = deriveRelations(classes, [])

    expect(relations).toContainEqual({ from: 'App.Order', to: 'App.EntityBase', type: 'inherit' })
    expect(relations).toContainEqual({ from: 'App.OrderRepository', to: 'App.IOrderRepository', type: 'implement' })
    expect(relations).toContainEqual({
      from: 'App.OrderService',
      to: 'App.IOrderRepository',
      type: 'assoc',
      label: 'repository',
    })
  })

  it('연관은 필드·프로퍼티만 본다. 메서드 반환 타입은 선을 만들지 않는다', () => {
    const relations = deriveRelations(classes, [])

    expect(relations.some((relation) => relation.type === 'assoc' && relation.to === 'App.Order')).toBe(false)
  })

  it('의존은 다른 관계에 이미 있는 쌍을 빼고 남은 호출 엣지만 남긴다', () => {
    const relations = deriveRelations(classes, [
      // 필드로 들고 있으면서 호출도 하는 관계는 연관 하나로만 그린다.
      { from: 'App.OrderService', to: 'App.IOrderRepository' },
      { from: 'App.OrderService', to: 'App.Reporter' },
    ])

    const depend = relations.filter((relation) => relation.type === 'depend')
    expect(depend).toEqual([{ from: 'App.OrderService', to: 'App.Reporter', type: 'depend' }])
  })

  it('자기 자신·목록에 없는 클래스로 향하는 관계는 버린다', () => {
    const relations = deriveRelations(
      [graphClass('App.Loop', { baseTypeId: 'App.Loop', interfaceIds: ['App.Missing'] })],
      [{ from: 'App.Loop', to: 'App.Missing' }])

    expect(relations).toHaveLength(0)
  })
})
