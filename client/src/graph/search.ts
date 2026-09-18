import type { GraphDocument } from '../types/graph'

/** 찾기 결과 한 줄. `kind`로 선택했을 때 어디로 갈지 정한다. */
export interface SearchHit {
  kind: 'entryPoint' | 'class' | 'method'
  /** 선택에 쓰는 값. 진입점은 endpointId, 클래스는 classId, 메서드는 노드 ID 또는 멤버 키다. */
  id: string
  /** 화면에 보이는 이름. */
  label: string
  /** 한 줄 아래에 붙는 부가 설명(라우트·네임스페이스·시그니처). */
  detail: string
}

export interface SearchResult {
  entryPoints: SearchHit[]
  classes: SearchHit[]
  methods: SearchHit[]
  /** 상한에 걸려 잘린 결과가 있는지. "이게 전부"로 읽히면 안 된다. */
  truncated: boolean
}

const empty: SearchResult = { entryPoints: [], classes: [], methods: [], truncated: false }

/** `OrderService.CreateOrderAsync(int)` → `createorderasync`. 순위를 매길 때만 쓴다. */
function simpleName(label: string): string {
  const withoutArguments = label.split('(')[0]
  const segments = withoutArguments.split('.')
  return (segments[segments.length - 1] ?? withoutArguments).toLowerCase()
}

/**
 * 진입점·클래스·메서드를 한 번에 찾는다.
 * 파일 트리 탐색기를 만들지 않고(설계 제약 1) 기존 두 탐색 축 안에서 검색만 넓히는 것이 목적이다.
 *
 * 대소문자를 무시하고, **접두 일치를 먼저** 보여 준다 —
 * `CreateOrderAsync`를 찾을 때 `TryCreateOrderAsyncCore`가 위에 오면 쓸모가 없다.
 * React에 의존하지 않는 순수 함수라 테스트로 정렬과 상한을 고정할 수 있다.
 */
export function searchGraph(graph: GraphDocument | null, query: string, limit = 20): SearchResult {
  const needle = query.trim().toLowerCase()
  if (!graph || needle.length === 0) return empty

  let truncated = false

  /**
   * 접두 일치 → 포함 → 이름순. 같은 순위 안에서는 짧은 이름이 먼저다(더 정확한 일치일 가능성이 높다).
   * 순위는 <b>홑이름</b>으로 매긴다. 노드 라벨은 `OrderService.CreateOrderAsync(int)`처럼 한정돼 있어
   * 전체 라벨로 접두를 보면 메서드 이름으로 찾은 것이 절대 위로 오지 않는다.
   */
  const rank = (name: string) => (simpleName(name).startsWith(needle) ? 0 : 1)
  const take = (hits: SearchHit[]) => {
    const sorted = hits.sort((left, right) =>
      rank(left.label) - rank(right.label) ||
      left.label.length - right.label.length ||
      left.label.localeCompare(right.label))
    if (sorted.length > limit) truncated = true
    return sorted.slice(0, limit)
  }

  const entryPoints = take(graph.entryPoints
    .filter((item) =>
      item.route.toLowerCase().includes(needle) ||
      item.httpMethod.toLowerCase().includes(needle) ||
      item.handlerDisplayName.toLowerCase().includes(needle))
    .map((item) => ({
      kind: 'entryPoint' as const,
      id: item.endpointId,
      label: `${item.httpMethod} ${item.route}`,
      detail: item.handlerDisplayName,
    })))

  const classes = take(graph.classes
    .filter((item) =>
      item.name.toLowerCase().includes(needle) ||
      item.namespace.toLowerCase().includes(needle))
    .map((item) => ({
      kind: 'class' as const,
      id: item.id,
      label: item.name,
      detail: item.namespace,
    })))

  // 노드(실행 가능한 메서드)를 먼저 담고, 노드가 없는 멤버(인터페이스 선언 등)를 뒤에 채운다.
  const methodHits: SearchHit[] = []
  const seen = new Set<string>()
  for (const node of graph.nodes) {
    if (!node.displayName.toLowerCase().includes(needle) && !node.id.toLowerCase().includes(needle)) continue
    seen.add(node.id)
    methodHits.push({
      kind: 'method',
      id: node.id,
      label: node.displayName,
      detail: node.classId ?? node.file,
    })
  }

  for (const graphClass of graph.classes) {
    for (const member of graphClass.members) {
      if (member.memberKind === 'Field' && !member.name.toLowerCase().includes(needle)) continue
      const signature = member.signature ?? member.name
      if (!member.name.toLowerCase().includes(needle) && !signature.toLowerCase().includes(needle)) continue
      const key = `${graphClass.id}.${signature}`
      if (seen.has(key)) continue
      seen.add(key)
      methodHits.push({
        kind: 'method',
        id: key,
        label: member.name,
        detail: `${graphClass.name} · ${signature}`,
      })
    }
  }

  return { entryPoints, classes, methods: take(methodHits), truncated }
}
