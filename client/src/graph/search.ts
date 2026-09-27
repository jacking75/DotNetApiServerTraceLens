import type { GraphDocument } from '../types/graph'

/** 찾기 결과 한 줄. `kind`로 선택했을 때 어디로 갈지 정한다. */
export interface SearchHit {
  kind: 'entryPoint' | 'class' | 'method' | 'rule' | 'finding' | 'file'
  /**
   * 선택에 쓰는 값.
   * 진입점은 endpointId, 클래스는 classId, 메서드는 **그래프 노드 ID**,
   * 규칙은 ruleId, 점검 결과는 findingId, 파일은 상대 경로다.
   */
  id: string
  /** 화면에 보이는 이름. */
  label: string
  /** 한 줄 아래에 붙는 부가 설명(라우트·네임스페이스·시그니처). */
  detail: string
  /**
   * 인터페이스 멤버를 골랐을 때 갈 수 있는 구현 노드.
   * 비어 있지 않으면 화면이 "구현체 N개"를 펼쳐 보여 준다.
   */
  implementations?: Array<{ id: string; label: string }>
}

export interface SearchResult {
  entryPoints: SearchHit[]
  classes: SearchHit[]
  methods: SearchHit[]
  rules: SearchHit[]
  files: SearchHit[]
  /** 상한에 걸려 잘린 결과가 있는지. "이게 전부"로 읽히면 안 된다. */
  truncated: boolean
}

const empty: SearchResult = {
  entryPoints: [], classes: [], methods: [], rules: [], files: [], truncated: false,
}

/** 접두사 필터. 범위를 좁히지 못하면 결과 20개가 모두 같은 종류로 차 버린다. */
export type SearchScope = 'all' | 'entryPoint' | 'namespace' | 'rule' | 'file'

export interface ParsedQuery {
  scope: SearchScope
  /** 진입점 범위일 때의 HTTP 동사. 없으면 빈 문자열이다. */
  httpMethod: string
  needle: string
}

const httpVerbs = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS']

/** 최근 항목은 현재 분석에서 다시 확인한다. 삭제된 항목과 잘못된 저장값은 표시하지 않는다. */
export function resolveRecentHits(graph: GraphDocument | null, recent: readonly string[]): SearchHit[] {
  if (!graph) return []
  return [...new Set(recent)].flatMap((value): SearchHit[] => {
    const colon = value.indexOf(':')
    if (colon < 1) return []
    const kind = value.slice(0, colon)
    const id = value.slice(colon + 1)
    if (kind === 'entryPoint') {
      const item = graph.entryPoints.find((entry) => entry.endpointId === id)
      return item ? [{ kind, id, label: `${item.httpMethod} ${item.route}`, detail: item.handlerDisplayName }] : []
    }
    if (kind === 'method') {
      const item = graph.nodes.find((node) => node.id === id)
      return item ? [{ kind, id, label: item.displayName, detail: item.classId ?? item.file }] : []
    }
    if (kind === 'class') {
      const item = graph.classes.find((entry) => entry.id === id)
      return item ? [{ kind, id, label: item.name, detail: item.namespace }] : []
    }
    if (kind === 'file') {
      return graph.nodes.some((node) => node.file === id)
        ? [{ kind, id, label: id.split(/[\\/]/).pop() ?? id, detail: id }] : []
    }
    if (kind === 'rule') {
      const item = [...(graph.dataAccess?.rules ?? []), ...(graph.middleware?.rules ?? [])].find((rule) => rule.id === id)
      return item ? [{ kind, id, label: id, detail: item.name }] : []
    }
    if (kind === 'finding') {
      const item = [...(graph.dataAccess?.findings ?? []), ...(graph.middleware?.findings ?? [])].find((finding) => finding.id === id)
      return item ? [{ kind, id, label: `${item.ruleId} ${item.symbol}`, detail: item.what }] : []
    }
    return []
  })
}

/**
 * 입력에서 접두사 필터를 떼어 낸다.
 * `GET /orders` → 진입점 중 GET만, `ns:Shop.Orders` → 네임스페이스, `rule:COMP` → 규칙, `file:Order` → 파일.
 */
export function parseQuery(query: string): ParsedQuery {
  const text = query.trim()
  const upper = text.toUpperCase()
  for (const verb of httpVerbs) {
    if (upper.startsWith(`${verb} `)) {
      return { scope: 'entryPoint', httpMethod: verb, needle: text.slice(verb.length + 1).trim().toLowerCase() }
    }
  }

  for (const [prefix, scope] of [['ns:', 'namespace'], ['rule:', 'rule'], ['file:', 'file']] as const) {
    if (text.toLowerCase().startsWith(prefix)) {
      return { scope, httpMethod: '', needle: text.slice(prefix.length).trim().toLowerCase() }
    }
  }

  return { scope: 'all', httpMethod: '', needle: text.toLowerCase() }
}

/** `OrderService.CreateOrderAsync(int)` → `createorderasync`. 순위를 매길 때만 쓴다. */
function simpleName(label: string): string {
  const withoutArguments = label.split('(')[0]
  const segments = withoutArguments.split('.')
  return (segments[segments.length - 1] ?? withoutArguments).toLowerCase()
}

/**
 * 진입점·클래스·메서드·규칙·파일을 한 번에 찾는다.
 * 파일 트리 탐색기를 만들지 않고(설계 제약 1) 기존 두 탐색 축 안에서 검색만 넓히는 것이 목적이다.
 *
 * **메서드 그룹에는 그래프 노드만 넣는다.** 노드가 없는 멤버(인터페이스 선언·프로퍼티·record 위치 인자)를
 * 메서드로 보여 주면 고른 뒤 상세가 "이 메서드는 마지막 분석 결과에 없다"로 끝난다 —
 * 찾기가 막다른 길로 보내면 다시 쓰지 않는다. 그런 멤버는 클래스 결과의 부제로만 남기고,
 * 인터페이스 멤버는 구현 노드를 함께 달아 고르면 그 구현으로 간다.
 *
 * 대소문자를 무시하고, **접두 일치를 먼저** 보여 준다 —
 * `CreateOrderAsync`를 찾을 때 `TryCreateOrderAsyncCore`가 위에 오면 쓸모가 없다.
 * React에 의존하지 않는 순수 함수라 테스트로 정렬과 상한을 고정할 수 있다.
 */
export function searchGraph(graph: GraphDocument | null, query: string, limit = 20): SearchResult {
  const { scope, httpMethod, needle } = parseQuery(query)
  if (!graph || (needle.length === 0 && httpMethod.length === 0)) return empty

  let truncated = false

  /**
   * 접두 일치 → 포함 → 이름순. 같은 순위 안에서는 짧은 이름이 먼저다(더 정확한 일치일 가능성이 높다).
   * 순위는 <b>홑이름</b>으로 매긴다. 노드 라벨은 `OrderService.CreateOrderAsync(int)`처럼 한정돼 있어
   * 전체 라벨로 접두를 보면 메서드 이름으로 찾은 것이 절대 위로 오지 않는다.
   * 정확히 같은 이름이면 무조건 맨 위다.
   */
  const rank = (name: string) => {
    const simple = simpleName(name)
    if (simple === needle) return 0
    return simple.startsWith(needle) ? 1 : 2
  }

  const take = (hits: SearchHit[]) => {
    const sorted = hits.sort((left, right) =>
      rank(left.label) - rank(right.label) ||
      left.label.length - right.label.length ||
      left.label.localeCompare(right.label))
    if (sorted.length > limit) truncated = true
    return sorted.slice(0, limit)
  }

  const matches = (value: string) => needle.length === 0 || value.toLowerCase().includes(needle)

  const entryPoints = scope === 'all' || scope === 'entryPoint'
    ? take(graph.entryPoints
      .filter((item) => httpMethod.length === 0 || item.httpMethod.toUpperCase() === httpMethod)
      .filter((item) =>
        matches(item.route) ||
        (httpMethod.length === 0 && matches(item.httpMethod)) ||
        matches(item.handlerDisplayName))
      .map((item) => ({
        kind: 'entryPoint' as const,
        id: item.endpointId,
        label: `${item.httpMethod} ${item.route}`,
        detail: item.handlerDisplayName,
      })))
    : []

  const classes = scope === 'all' || scope === 'namespace'
    ? take(graph.classes
      .filter((item) => scope === 'namespace'
        ? matches(item.namespace)
        : matches(item.name) || matches(item.namespace) ||
          item.members.some((member) => matches(member.name)))
      .map((item) => {
        // 노드가 없는 멤버는 클래스의 부제로만 보인다. 고르면 클래스가 선택된다.
        const memberHits = item.members.filter((member) => needle.length > 0 && matches(member.name))
        return {
          kind: 'class' as const,
          id: item.id,
          label: item.name,
          detail: memberHits.length > 0
            ? `${item.namespace} · ${memberHits.slice(0, 3).map((member) => member.name).join(', ')}`
            : item.namespace,
        }
      }))
    : []

  // 메서드 그룹에는 실행 가능한 그래프 노드만 담는다.
  const methods: SearchHit[] = []
  if (scope === 'all') {
    const nodeIds = new Set(graph.nodes.map((node) => node.id))
    for (const node of graph.nodes) {
      if (!matches(node.displayName) && !matches(node.id)) continue
      methods.push({
        kind: 'method',
        id: node.id,
        label: node.displayName,
        detail: node.classId ?? node.file,
      })
    }

    // 인터페이스 멤버는 노드가 없다. 구현 노드를 달아 고르면 그 구현으로 가게 한다.
    for (const graphClass of graph.classes) {
      if (graphClass.typeKind !== 'Interface') continue
      for (const member of graphClass.members) {
        if (!matches(member.name)) continue
        const implementations = graph.classes
          .filter((item) => item.interfaceIds?.includes(graphClass.id))
          .map((item) => ({
            id: [...nodeIds].find((id) =>
              id.startsWith(`${item.id}.`) && simpleName(id) === member.name.toLowerCase()) ?? '',
            label: `${item.name}.${member.name}`,
          }))
          .filter((item) => item.id.length > 0)
        if (implementations.length === 0) continue
        methods.push({
          kind: 'method',
          id: implementations[0].id,
          label: `${graphClass.name}.${member.name}`,
          detail: graphClass.namespace,
          implementations,
        })
      }
    }
  }

  const rules = scope === 'all' || scope === 'rule'
    ? take([
      ...[...(graph.dataAccess?.rules ?? []), ...(graph.middleware?.rules ?? [])]
        .filter((item) => matches(item.id) || matches(item.name))
        .map((item) => ({ kind: 'rule' as const, id: item.id, label: item.id, detail: item.name })),
      ...[...(graph.dataAccess?.findings ?? []), ...(graph.middleware?.findings ?? [])]
        .filter((item) => matches(item.ruleId) || matches(item.what) || matches(item.symbol))
        .map((item) => ({
          kind: 'finding' as const,
          id: item.id,
          label: `${item.ruleId} ${item.symbol}`,
          detail: item.what,
        })),
    ])
    : []

  const files = scope === 'all' || scope === 'file'
    ? take([...new Set(graph.nodes.map((node) => node.file))]
      .filter((file) => matches(file))
      .map((file) => ({ kind: 'file' as const, id: file, label: file.split(/[\\/]/).pop() ?? file, detail: file })))
    : []

  return { entryPoints, classes, methods: take(methods), rules, files, truncated }
}
