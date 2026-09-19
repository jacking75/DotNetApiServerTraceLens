import type { EntryPoint } from '../types/graph'

/**
 * 진입점 목록의 그룹화·정렬·접힘 규칙. 세 보기(호출 흐름·전체 흐름도·DB·캐시)가 같은 규칙을 쓰도록
 * React에 의존하지 않는 순수 함수로 둔다.
 */

/** 무엇으로 묶을지. `project`는 실행 프로젝트가 둘 이상일 때만 쓴다(T65). */
export type EntryGroupBy = 'controller' | 'project'

/** 목록 머리의 정렬 토글. 저장값이 이 중에 없으면 `route`로 돌린다. */
export type EntrySort = 'route' | 'method' | 'source'

export const entrySorts: readonly EntrySort[] = ['route', 'method', 'source']

export interface EntryGroup {
  /** 그룹 키. 접힘 상태와 React key로 쓴다. */
  key: string
  /** 화면에 보이는 이름. 컨트롤러는 짧은 클래스 이름, Minimal API는 접두사나 등록 파일이다. */
  label: string
  /**
   * 바깥 구획 이름(실행 프로젝트). `groupBy: 'project'`일 때만 채운다.
   * 같은 값이 이어지는 동안 한 구획이라 화면은 값이 바뀔 때만 머리글을 그린다.
   */
  section: string | null
  /** 이 그룹이 Minimal API 등록인지. 목록 머리에 출처 표시를 붙인다. */
  source: 'Controller' | 'MinimalApi' | 'Mixed'
  items: EntryPoint[]
}

/**
 * 이 개수를 넘는 그룹이 있으면 접힌 채로 시작한다.
 * 20개까지는 한 화면에 들어오므로 매번 열어야 하는 쪽이 더 비싸다.
 */
export const collapseGroupsAbove = 20

/** HTTP 동사 정렬 순서. 알파벳순이면 DELETE가 GET보다 앞이라 읽는 순서와 어긋난다. */
const methodOrder: Record<string, number> = {
  GET: 0, HEAD: 1, POST: 2, PUT: 3, PATCH: 4, DELETE: 5, OPTIONS: 6,
}

/** 등록 파일 경로에서 파일 이름만 남긴다(`Endpoints/ReportEndpoints.cs` → `ReportEndpoints`). */
export function registrationFileLabel(path: string): string {
  const name = path.split(/[\\/]/).pop() ?? path
  return name.replace(/\.cs$/i, '') || path
}

/**
 * 한 진입점의 그룹 키를 고른다.
 * - 컨트롤러: 선언 클래스(`classIdOfMethod`). 못 찾으면 `unknownLabel`.
 * - Minimal API: `MapGroup` 접두사. 없으면 등록 파일 이름.
 *
 * 두 기준이 섞이지 않도록 Minimal API 키에는 접두사를 붙인다(같은 이름의 클래스와 겹치지 않게).
 */
export function groupKeyOf(
  entryPoint: EntryPoint,
  classIdOfMethod: ((nodeId: string) => string | undefined) | undefined,
  unknownLabel: string,
): { key: string; label: string } {
  if (entryPoint.source === 'MinimalApi') {
    const prefix = (entryPoint.groupKey ?? '').trim()
    if (prefix.length > 0) return { key: `map:${prefix}`, label: prefix }
    const file = (entryPoint.registrationFile ?? '').trim()
    if (file.length > 0) return { key: `file:${file}`, label: registrationFileLabel(file) }
    return { key: `map:${unknownLabel}`, label: unknownLabel }
  }

  const classId = classIdOfMethod?.(entryPoint.nodeId)
  if (!classId) return { key: `cls:${unknownLabel}`, label: unknownLabel }
  return { key: `cls:${classId}`, label: classId.split('.').pop() ?? classId }
}

function compare(left: EntryPoint, right: EntryPoint, sort: EntrySort): number {
  if (sort === 'method') {
    const delta = (methodOrder[left.httpMethod] ?? 9) - (methodOrder[right.httpMethod] ?? 9)
    if (delta !== 0) return delta
    return left.route.localeCompare(right.route)
  }

  if (sort === 'source') {
    if (left.source !== right.source) return left.source === 'Controller' ? -1 : 1
    return left.route.localeCompare(right.route)
  }

  const byRoute = left.route.localeCompare(right.route)
  if (byRoute !== 0) return byRoute
  return (methodOrder[left.httpMethod] ?? 9) - (methodOrder[right.httpMethod] ?? 9)
}

export interface GroupOptions {
  groupBy: EntryGroupBy
  sort: EntrySort
  classIdOfMethod?: (nodeId: string) => string | undefined
  unknownLabel: string
}

/**
 * 진입점을 그룹으로 묶고 각 그룹 안을 정렬한다. 그룹 순서는 이름순이다(삽입 순서는 재분석마다 흔들린다).
 * `groupBy: 'project'`면 컨트롤러 그룹 위에 프로젝트 구획을 한 겹 더 둔다.
 */
export function groupEntryPoints(
  entryPoints: readonly EntryPoint[],
  { groupBy, sort, classIdOfMethod, unknownLabel }: GroupOptions,
): EntryGroup[] {
  const grouped = new Map<string, EntryGroup>()
  for (const entryPoint of entryPoints) {
    const inner = groupKeyOf(entryPoint, classIdOfMethod, unknownLabel)
    const section = groupBy === 'project' ? entryPoint.projectKey || unknownLabel : null
    const key = section === null ? inner.key : `prj:${section}|${inner.key}`
    const existing = grouped.get(key)
    if (existing) {
      existing.items.push(entryPoint)
      if (existing.source !== entryPoint.source) existing.source = 'Mixed'
      continue
    }

    grouped.set(key, { key, label: inner.label, section, source: entryPoint.source, items: [entryPoint] })
  }

  const groups = [...grouped.values()]
  for (const group of groups) group.items.sort((left, right) => compare(left, right, sort))
  groups.sort((left, right) =>
    (left.section ?? '').localeCompare(right.section ?? '') || left.label.localeCompare(right.label))
  return groups
}

/**
 * 그룹이 많으면 접힌 채로 시작한다. 검색 중에는 결과가 보여야 하므로 항상 펼친다.
 * `expanded`는 "기본값을 뒤집은 그룹"이므로 기본이 펼침일 때와 접힘일 때 의미가 반대다.
 */
export function collapsedByDefault(groupCount: number, searching: boolean): boolean {
  return groupCount > collapseGroupsAbove && !searching
}

/** 한 그룹이 지금 열려 있는지. `collapsedByDefault`가 false면 `expanded`에 든 것이 접힌 것이다. */
export function isGroupOpen(
  key: string,
  expanded: ReadonlySet<string>,
  collapsed: boolean,
): boolean {
  return collapsed ? expanded.has(key) : !expanded.has(key)
}

/** 선택한 항목이 든 그룹을 연다. 이미 열려 있으면 같은 집합을 그대로 돌려준다(리렌더를 만들지 않는다). */
export function openGroup(
  key: string,
  expanded: ReadonlySet<string>,
  collapsed: boolean,
): ReadonlySet<string> {
  if (isGroupOpen(key, expanded, collapsed)) return expanded
  const next = new Set(expanded)
  if (collapsed) next.add(key)
  else next.delete(key)
  return next
}

/** 그룹 접힘을 뒤집는다. */
export function toggleGroup(key: string, expanded: ReadonlySet<string>): ReadonlySet<string> {
  const next = new Set(expanded)
  if (!next.delete(key)) next.add(key)
  return next
}

/** 선택한 엔드포인트가 속한 그룹 키. 없으면 null이다. */
export function findGroupKey(groups: readonly EntryGroup[], endpointId: string | null): string | null {
  if (!endpointId) return null
  for (const group of groups) {
    if (group.items.some((item) => item.endpointId === endpointId)) return group.key
  }

  return null
}

/**
 * 라우트가 좁은 목록에서 잘릴 때 남길 부분. 뒤쪽(`/{customerId}`)이 구분에 더 쓸모 있으므로
 * 공통 접두사를 그룹 머리로 올리고 남은 부분만 보여 준다.
 */
export function commonRoutePrefix(routes: readonly string[]): string {
  if (routes.length < 2) return ''
  const split = routes.map((route) => route.split('/').filter((part) => part.length > 0))
  const first = split[0]
  let shared = 0
  while (shared < first.length) {
    const segment = first[shared]
    // 마지막 조각까지 다 먹으면 라우트가 통째로 사라진다. 최소 한 조각은 남긴다.
    if (split.some((parts) => parts.length <= shared + 1 || parts[shared] !== segment)) break
    shared += 1
  }

  return shared === 0 ? '' : `/${first.slice(0, shared).join('/')}`
}

/** 진입점 필터. 숨긴 API는 기본으로 빼고 토글로만 보여 준다. */
export function filterEntryPoints(
  entryPoints: readonly EntryPoint[],
  keyword: string,
  showHidden: boolean,
  classIdOfMethod?: (nodeId: string) => string | undefined,
): EntryPoint[] {
  const needle = keyword.trim().toLowerCase()
  return entryPoints.filter((entryPoint) => {
    if (!showHidden && entryPoint.hiddenFromApiExplorer) return false
    if (needle.length === 0) return true
    return entryPoint.route.toLowerCase().includes(needle) ||
      entryPoint.httpMethod.toLowerCase().includes(needle) ||
      (classIdOfMethod?.(entryPoint.nodeId) ?? '').toLowerCase().includes(needle) ||
      entryPoint.handlerDisplayName.toLowerCase().includes(needle)
  })
}
