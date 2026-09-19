import type { GraphClass, GraphClassMember, Selection } from '../types/graph'
import { layoutPackages, type PlacedPackage } from './packageLayout'
import { countRelations, type ClassRelation, type RelationType } from './relations'
import { layoutTree, type PlacedBox, type RelationPath, type TreeDirection } from './treeLayout'

export type DiagramLayoutMode = 'tree' | 'package'

export interface ClassDiagramOptions {
  layout: DiagramLayoutMode
  treeDirection: TreeDirection
  showFields: boolean
  showMethods: boolean
  relations: Record<RelationType, boolean>
  hiddenNamespaces: ReadonlySet<string>
  focusOnly: boolean
  selection: Selection | null
  /** 패키지 배치의 줄바꿈 기준이 되는 캔버스 폭(줌을 나눈 값). */
  availableWidth: number
  namespacePrefix: string
}

export interface ClassDiagramView {
  visible: GraphClass[]
  relations: ClassRelation[]
  boxes: Map<string, PlacedBox>
  packages: PlacedPackage[]
  paths: RelationPath[]
  size: { width: number; height: number }
  counts: Record<RelationType, number>
}

/** 클래스가 이보다 많으면 멤버를 접고 의존을 끈 상태로 시작한다. */
export const largeSolutionThreshold = 150

const visibilitySigns = { Public: '+', Protected: '#', Internal: '~', Private: '−' } as const

export const isFieldLike = (member: GraphClassMember) =>
  member.memberKind === 'Field' || member.memberKind === 'Property'

export const memberSign = (member: GraphClassMember) => visibilitySigns[member.accessibility] ?? '+'

/**
 * 멤버 줄의 선택 키. 실행 가능한 멤버는 노드 id를 그대로 쓰고,
 * 인터페이스 메서드처럼 노드가 없는 멤버에는 선언 위치로 만든 키를 준다.
 */
export const memberKey = (classId: string, member: GraphClassMember) =>
  member.nodeId ?? `${classId}#${member.memberKind}.${member.name}@${member.startLine}`

/** UML 한 줄의 표시 문자열. 메서드는 `이름(인자) : 반환타입` 형태다. */
export function memberText(member: GraphClassMember): string {
  const head = isFieldLike(member) ? member.name : member.signature ?? `${member.name}()`
  return member.type ? `${head} : ${member.type}` : head
}

/** 모든 네임스페이스가 공유하는 앞부분. 캡션에서 잘라내 이름이 길어지는 것을 막는다. */
export function commonNamespacePrefix(namespaces: string[]): string {
  const candidates = namespaces.filter((item) => item.length > 0).map((item) => item.split('.'))
  if (candidates.length < 2) return ''
  const first = candidates[0]
  let shared = 0
  while (shared < first.length - 1 && candidates.every((parts) => parts.length > shared + 1 && parts[shared] === first[shared])) {
    shared += 1
  }

  return shared === 0 ? '' : `${first.slice(0, shared).join('.')}.`
}

/**
 * 네임스페이스 표시 이름. 공통 접두사를 떼어 낸다.
 * 네임스페이스가 없는 타입(전역)의 라벨은 번역이 필요하므로 호출자가 넘긴다 —
 * 여기서 한국어를 만들면 영어 화면에도 그대로 나온다.
 */
export function shortenNamespace(namespace: string, prefix: string, globalLabel = ''): string {
  if (!namespace) return globalLabel
  return prefix && namespace.startsWith(prefix) ? namespace.slice(prefix.length) : namespace
}

export function stereotypeOf(graphClass: GraphClass): string | null {
  switch (graphClass.typeKind) {
    case 'Interface': return 'interface'
    case 'Record': return 'record'
    case 'Struct': return 'struct'
    default: return graphClass.isStatic ? 'static' : null
  }
}

/** 이름 위에 붙는 작은 캡션. 트리 배치에는 패키지 박스가 없어서 네임스페이스를 여기에 적는다. */
export function captionOf(graphClass: GraphClass, layout: DiagramLayoutMode, prefix: string): string {
  const parts: string[] = []
  const stereotype = stereotypeOf(graphClass)
  if (stereotype) parts.push(`«${stereotype}»`)
  if (layout === 'tree') parts.push(shortenNamespace(graphClass.namespace, prefix))
  return parts.join(' · ')
}

export function classMembers(graphClass: GraphClass) {
  const fields = graphClass.members.filter(isFieldLike)
  const methods = graphClass.members.filter((member) => !isFieldLike(member))
  return { fields, methods }
}

/** 박스 크기. 폭은 가장 긴 멤버 문자열로 잡고 184~336px로 클램프한다. */
export function umlSize(graphClass: GraphClass, options: ClassDiagramOptions): { w: number; h: number } {
  const caption = captionOf(graphClass, options.layout, options.namespacePrefix)
  const { fields, methods } = classMembers(graphClass)
  const texts = [graphClass.name, caption]
  if (options.showFields) for (const member of fields) texts.push(memberText(member))
  if (options.showMethods) for (const member of methods) texts.push(memberText(member))

  const longest = Math.max(...texts.map((text) => text.length))
  const w = Math.max(184, Math.min(336, longest * 6.05 + 30))
  let h = caption ? 44 : 30
  if (options.showFields) h += 6 + Math.max(1, fields.length) * 16
  if (options.showMethods) h += 6 + Math.max(1, methods.length) * 16
  return { w, h }
}

/** 지금 켜 둔 필터로 그릴 클래스와 관계를 고른다. */
export function diagramScope(
  classes: GraphClass[],
  relations: ClassRelation[],
  options: ClassDiagramOptions,
  classIdOfMethod: (nodeId: string) => string | undefined,
): { visible: GraphClass[]; relations: ClassRelation[] } {
  let visible = classes.filter((item) => !options.hiddenNamespaces.has(item.namespace))
  const selection = options.selection
  if (options.focusOnly && selection && selection.type !== 'namespace') {
    const center = selection.type === 'class' ? selection.key : classIdOfMethod(selection.key)
    if (center) {
      // 켜 둔 관계 종류로만 이웃을 찾는다. 안 그러면 선 없는 박스가 남는다.
      const near = new Set([center])
      for (const relation of relations) {
        if (!options.relations[relation.type]) continue
        if (relation.from === center) near.add(relation.to)
        if (relation.to === center) near.add(relation.from)
      }

      visible = classes.filter((item) => near.has(item.id))
    }
  }

  const ids = new Set(visible.map((item) => item.id))
  return {
    visible,
    relations: relations.filter((relation) =>
      options.relations[relation.type] && ids.has(relation.from) && ids.has(relation.to)),
  }
}

export function buildClassDiagram(
  classes: GraphClass[],
  allRelations: ClassRelation[],
  options: ClassDiagramOptions,
  classIdOfMethod: (nodeId: string) => string | undefined,
): ClassDiagramView {
  const scope = diagramScope(classes, allRelations, options, classIdOfMethod)
  const nodes = scope.visible.map((item) => ({ id: item.id, ...umlSize(item, options) }))
  const layout = options.layout === 'tree'
    ? { ...layoutTree(nodes, scope.relations, { direction: options.treeDirection }), packages: [] as PlacedPackage[] }
    : layoutPackages(
      nodes,
      new Map(scope.visible.map((item) => [item.id, item.namespace])),
      scope.relations,
      options.availableWidth)

  return {
    visible: scope.visible,
    relations: scope.relations,
    boxes: layout.boxes,
    packages: layout.packages,
    paths: layout.paths,
    size: layout.size,
    counts: countRelations(scope.relations),
  }
}
