import type { RelationType } from '../graph/relations'
import type { Selection, ViewMode } from '../types/graph'
import type { ClassMapState } from './viewState'

/**
 * URL 해시로 재현할 수 있는 화면 위치.
 * 루프백 URL이라 외부에 공개되지 않고(설계 제약 4), 팀 공유는 기존 공유 보고서가 맡는다.
 * 여기서 담는 것은 "어느 화면을 보고 있었나"뿐이고 소스나 분석 결과는 담지 않는다.
 *
 * 히스토리 규칙: **보기·진입점·포커스·하위 탭이 바뀌면 `pushState`, 선택만 바뀌면 `replaceState`**다.
 * 노드 20개를 눌러 본 뒤 뒤로 가기를 20번 눌러야 이전 보기로 나가는 일이 없어야 한다.
 */
export interface UrlState {
  view: ViewMode
  /** 호출 흐름에서 고른 진입점의 엔드포인트 ID. */
  entry: string | null
  /** 호출 흐름에서 파고든 노드 스택. */
  focus: string[]
  selection: Selection | null
  /** DB·캐시 또는 미들웨어 보기의 하위 탭. */
  sub: string | null
  /** 전체 흐름도에서 고른 진입점 노드 ID. */
  flowEntry: string | null
  /** 전체 흐름도의 깊이. */
  depth: number | null
  /** 클래스 다이어그램 토글. 비트 하나씩 담아 짧게 쓴다. */
  classMap: Partial<ClassMapState> | null
  /** 호출 흐름 창의 방향. */
  direction: 'down' | 'up' | null
}

/** 선택 종류를 해시에 쓰는 짧은 이름으로 줄인다. 되돌릴 수 있어야 하므로 표를 양방향으로 쓴다. */
const selectionTypes: Record<string, Selection['type']> = {
  method: 'method',
  class: 'class',
  ns: 'namespace',
  startup: 'startupStep',
  finding: 'finding',
  path: 'writePath',
  source: 'dataSource',
  target: 'storageTarget',
  rule: 'rule',
  mw: 'middlewareStep',
  filter: 'mvcFilter',
}

const selectionCodes = Object.fromEntries(
  Object.entries(selectionTypes).map(([code, type]) => [type, code]),
) as Record<Selection['type'], string>

/** 클래스 다이어그램 토글 비트. 순서를 바꾸면 옛 링크가 다른 화면을 연다 — 뒤에만 더한다. */
const classMapBits: ReadonlyArray<{ bit: number; apply: (value: boolean) => Partial<ClassMapState> }> = [
  { bit: 1, apply: (value) => ({ showFields: value }) },
  { bit: 2, apply: (value) => ({ showMethods: value }) },
  { bit: 4, apply: (value) => ({ focusOnly: value }) },
]

const relationBits: ReadonlyArray<{ bit: number; relation: RelationType }> = [
  { bit: 8, relation: 'inherit' },
  { bit: 16, relation: 'implement' },
  { bit: 32, relation: 'assoc' },
  { bit: 64, relation: 'depend' },
]

function encodeClassMap(classMap: Partial<ClassMapState>): number {
  let bits = 0
  if (classMap.showFields) bits |= 1
  if (classMap.showMethods) bits |= 2
  if (classMap.focusOnly) bits |= 4
  for (const { bit, relation } of relationBits) {
    if (classMap.relations?.[relation]) bits |= bit
  }

  return bits
}

function decodeClassMap(bits: number): Partial<ClassMapState> {
  const result: Partial<ClassMapState> = {}
  for (const { bit, apply } of classMapBits) Object.assign(result, apply((bits & bit) !== 0))
  const relations = {} as Record<RelationType, boolean>
  for (const { bit, relation } of relationBits) relations[relation] = (bits & bit) !== 0
  result.relations = relations
  return result
}

/**
 * 화면 위치를 해시 문자열로 만든다.
 * 값이 없는 항목은 아예 넣지 않는다 — `#view=callflow&entry=&focus=`처럼 빈 값이 늘어서면 읽기 어렵다.
 */
export function serialize(state: UrlState): string {
  const parts = [`view=${state.view}`]
  if (state.entry) parts.push(`entry=${encodeURIComponent(state.entry)}`)
  if (state.focus.length > 0) parts.push(`focus=${state.focus.map(encodeURIComponent).join(',')}`)
  if (state.sub) parts.push(`sub=${encodeURIComponent(state.sub)}`)
  if (state.flowEntry) parts.push(`flowEntry=${encodeURIComponent(state.flowEntry)}`)
  if (state.depth !== null && state.depth !== undefined) parts.push(`depth=${state.depth}`)
  if (state.classMap) parts.push(`cm=${encodeClassMap(state.classMap)}`)
  if (state.direction === 'up') parts.push('dir=up')
  if (state.selection) {
    parts.push(`sel=${selectionCodes[state.selection.type]}:${encodeURIComponent(state.selection.key)}`)
  }

  return `#${parts.join('&')}`
}

const viewModes: ViewMode[] = ['callflow', 'classmap', 'flowmap', 'startup', 'data', 'middleware']

/**
 * 해시를 화면 위치로 되돌린다.
 * 모르는 키와 모르는 값은 무시한다 — 옛 링크나 손으로 고친 URL 때문에 화면이 깨지면 안 된다.
 * 되돌린 ID가 지금 그래프에 있는지는 호출자가 확인한다(여기서는 그래프를 모른다).
 */
export function parse(hash: string): Partial<UrlState> {
  const text = hash.startsWith('#') ? hash.slice(1) : hash
  if (text.length === 0) return {}

  const result: Partial<UrlState> = {}
  for (const part of text.split('&')) {
    const separator = part.indexOf('=')
    if (separator <= 0) continue
    const key = part.slice(0, separator)
    const value = part.slice(separator + 1)

    if (key === 'view') {
      const view = value as ViewMode
      if (viewModes.includes(view)) result.view = view
      continue
    }

    if (key === 'entry') {
      result.entry = decodeURIComponent(value)
      continue
    }

    if (key === 'focus') {
      result.focus = value.split(',').filter(Boolean).map(decodeURIComponent)
      continue
    }

    if (key === 'sub') {
      result.sub = decodeURIComponent(value)
      continue
    }

    if (key === 'flowEntry') {
      result.flowEntry = decodeURIComponent(value)
      continue
    }

    if (key === 'depth') {
      const depth = Number(value)
      if (Number.isInteger(depth) && depth >= 1 && depth <= 9) result.depth = depth
      continue
    }

    if (key === 'cm') {
      const bits = Number(value)
      if (Number.isInteger(bits) && bits >= 0) result.classMap = decodeClassMap(bits)
      continue
    }

    if (key === 'dir') {
      if (value === 'up' || value === 'down') result.direction = value
      continue
    }

    if (key === 'sel') {
      const colon = value.indexOf(':')
      if (colon <= 0) continue
      const type = selectionTypes[value.slice(0, colon)]
      if (!type) continue
      result.selection = { type, key: decodeURIComponent(value.slice(colon + 1)) } as Selection
    }
  }

  return result
}

/**
 * 이 두 상태 사이가 "구조 변경"인지. 구조 변경은 `pushState`, 선택만 다르면 `replaceState`다.
 * 규칙을 한곳에 두고 테스트로 고정한다.
 */
export function isStructuralChange(previous: Partial<UrlState> | null, next: Partial<UrlState>): boolean {
  if (!previous) return true
  return previous.view !== next.view ||
    (previous.entry ?? null) !== (next.entry ?? null) ||
    (previous.flowEntry ?? null) !== (next.flowEntry ?? null) ||
    (previous.sub ?? null) !== (next.sub ?? null) ||
    (previous.focus ?? []).join('|') !== (next.focus ?? []).join('|')
}
