import type { GraphClass, GraphDocument, GraphEdge } from '../types/graph'

export type RelationType = 'inherit' | 'implement' | 'assoc' | 'depend'

export interface ClassRelation {
  from: string
  to: string
  type: RelationType
  /** 연관 관계에서 그 타입을 들고 있는 멤버 이름. */
  label?: string
}

export const relationLabels: Record<RelationType, string> = {
  inherit: '상속',
  implement: '구현',
  assoc: '연관',
  depend: '의존',
}

/** 메서드 호출 엣지를 클래스 단위로 접는다. 같은 클래스 안의 호출은 관계도에서 의미가 없으므로 뺀다. */
export function collapseEdgesToClasses(graph: GraphDocument): GraphEdge[] {
  const ownerOf = new Map(graph.nodes.map((node) => [node.id, node.classId]))
  const classEdges = new Map<string, GraphEdge>()
  for (const edge of graph.edges) {
    const from = ownerOf.get(edge.from)
    const to = ownerOf.get(edge.to)
    if (!from || !to || from === to) continue
    const key = `${from} -> ${to}`
    // 한 클래스 쌍에 여러 호출이 접히면 가장 높은 확실도를 남긴다(직접 호출이 하나라도 있으면 확실).
    const confidence = classEdges.get(key)?.confidence === 'Certain' ? 'Certain' : edge.confidence
    classEdges.set(key, { from, to, confidence })
  }

  return [...classEdges.values()]
}

/**
 * 클래스 목록에서 UML 관계 네 종류를 파생한다. 서버는 관계를 계산하지 않는다 —
 * 관계 토글을 켜고 끌 때 즉시 반응해야 해서 클라이언트에서 만든다.
 *
 * 의존(호출)은 **앞의 세 종류에 이미 있는 (from, to) 쌍을 뺀 나머지**만 남긴다.
 * 필드로 들고 있으면서 호출도 하는 관계(대부분의 DI 주입)가 선 두 개로 겹쳐 그려지는 것을 막는 차감이다.
 */
export function deriveRelations(classes: GraphClass[], classEdges: GraphEdge[]): ClassRelation[] {
  const known = new Set(classes.map((item) => item.id))
  const relations: ClassRelation[] = []
  const seen = new Set<string>()
  const add = (from: string, to: string, type: RelationType, label?: string) => {
    const key = `${from}>${to}>${type}`
    if (from === to || !known.has(from) || !known.has(to) || seen.has(key)) return
    seen.add(key)
    relations.push(label ? { from, to, type, label } : { from, to, type })
  }

  for (const item of classes) {
    if (item.baseTypeId) add(item.id, item.baseTypeId, 'inherit')
    for (const interfaceId of item.interfaceIds) add(item.id, interfaceId, 'implement')
    // 연관은 "그 타입을 들고 있는" 관계라서 필드와 프로퍼티만 본다.
    // 메서드의 반환 타입까지 넣으면 DTO로 향하는 선이 폭발하고, 그 관계는 의존(호출)으로 이미 표현된다.
    for (const member of item.members) {
      if (member.memberKind !== 'Field' && member.memberKind !== 'Property') continue
      for (const typeId of member.typeIds) add(item.id, typeId, 'assoc', member.name)
    }
  }

  const linked = new Set(relations.map((relation) => `${relation.from}>${relation.to}`))
  for (const edge of classEdges) {
    if (!linked.has(`${edge.from}>${edge.to}`)) add(edge.from, edge.to, 'depend')
  }

  return relations
}

export function countRelations(relations: ClassRelation[]): Record<RelationType, number> {
  const counts: Record<RelationType, number> = { inherit: 0, implement: 0, assoc: 0, depend: 0 }
  for (const relation of relations) counts[relation.type] += 1
  return counts
}
