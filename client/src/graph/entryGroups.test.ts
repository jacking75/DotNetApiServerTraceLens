import { describe, expect, it } from 'vitest'
import {
  collapsedByDefault,
  commonRoutePrefix,
  findGroupKey,
  groupEntryPoints,
  isGroupOpen,
  openGroup,
} from './entryGroups'
import type { EntryPoint } from '../types/graph'

function entry(patch: Partial<EntryPoint> & { route: string }): EntryPoint {
  return {
    nodeId: `Shop.${patch.route}`,
    httpMethod: 'GET',
    endpointId: `${patch.httpMethod ?? 'GET'} ${patch.route}`,
    normalizedRoute: patch.route,
    source: 'Controller',
    handlerKind: 'Method',
    confidence: 'Certain',
    registrationFile: 'src/OrdersController.cs',
    registrationLine: 1,
    handlerDisplayName: 'Handler',
    projectKey: 'Shop',
    notes: [],
    groupKey: '',
    hiddenFromApiExplorer: false,
    authorization: null,
    ...patch,
  }
}

const unknown = '(알 수 없음)'

describe('entryGroups', () => {
  /**
   * 매일 첫 조작이 "진입점 고르기"다. 그룹이 몇 개 안 되는데 매번 열어야 하면 그 조작이 두 번이 된다.
   * 예전 코드는 `expanded.has(id) !== collapsedByDefault`라 짧은 목록에서 기본이 닫힘이었다(E1).
   */
  it('그룹이 20개 이하면 기본 펼침, 넘으면 기본 접힘이다', () => {
    expect(collapsedByDefault(3, false)).toBe(false)
    expect(collapsedByDefault(20, false)).toBe(false)
    expect(collapsedByDefault(21, false)).toBe(true)
    // 검색 중에는 결과가 보여야 하므로 항상 펼친다.
    expect(collapsedByDefault(50, true)).toBe(false)

    // 기본 펼침에서 `expanded`에 든 것이 접힌 것이고, 기본 접힘에서는 반대다.
    expect(isGroupOpen('a', new Set(), false)).toBe(true)
    expect(isGroupOpen('a', new Set(['a']), false)).toBe(false)
    expect(isGroupOpen('a', new Set(), true)).toBe(false)
    expect(isGroupOpen('a', new Set(['a']), true)).toBe(true)
  })

  /** 어떤 방법으로 진입점을 골라도 좌측 목록에서 강조가 보여야 한다. */
  it('선택한 항목의 그룹을 연다', () => {
    const groups = groupEntryPoints(
      [entry({ route: '/api/orders' })],
      { groupBy: 'controller', sort: 'route', classIdOfMethod: () => 'Shop.OrdersController', unknownLabel: unknown })

    const key = findGroupKey(groups, groups[0].items[0].endpointId)
    expect(key).toBe('cls:Shop.OrdersController')

    // 기본 펼침이면 `expanded`에서 빼야 열린다.
    expect(openGroup(key!, new Set([key!]), false)).toEqual(new Set())
    // 기본 접힘이면 더해야 열린다.
    expect(openGroup(key!, new Set(), true)).toEqual(new Set([key!]))
    // 이미 열려 있으면 같은 집합을 그대로 돌려준다(쓸데없는 리렌더를 만들지 않는다).
    const already = new Set<string>()
    expect(openGroup(key!, already, false)).toBe(already)
  })

  /**
   * Minimal API 진입점의 그룹 이름이 핸들러 선언 클래스(`Program`)면 의미가 없다.
   * `MapGroup` 접두사로 묶고, 접두사가 없으면 등록 파일로 묶는다.
   */
  it('Minimal API는 MapGroup 접두사로, 없으면 등록 파일로 묶는다', () => {
    const groups = groupEntryPoints(
      [
        entry({ route: '/api/v1/reports/daily', source: 'MinimalApi', groupKey: '/api/v1/reports' }),
        entry({ route: '/api/v1/reports/weekly', source: 'MinimalApi', groupKey: '/api/v1/reports' }),
        entry({ route: '/health', source: 'MinimalApi', registrationFile: 'src/Endpoints/HealthEndpoints.cs' }),
      ],
      { groupBy: 'controller', sort: 'route', classIdOfMethod: () => 'Program', unknownLabel: unknown })

    expect(groups.map((group) => group.label)).toEqual(['/api/v1/reports', 'HealthEndpoints'])
    expect(groups[0].items).toHaveLength(2)
  })

  /** 실행 프로젝트가 여럿이면 컨트롤러 그룹 위에 프로젝트 구획을 한 겹 더 둔다. */
  it('프로젝트로 묶으면 구획이 생긴다', () => {
    const groups = groupEntryPoints(
      [
        entry({ route: '/api/orders', projectKey: 'Shop.Api' }),
        entry({ route: '/jobs/run', projectKey: 'Shop.Worker' }),
      ],
      { groupBy: 'project', sort: 'route', classIdOfMethod: () => 'Shop.OrdersController', unknownLabel: unknown })

    expect(groups.map((group) => group.section)).toEqual(['Shop.Api', 'Shop.Worker'])
  })

  /** 긴 라우트가 좁은 목록에서 잘리면 뒤쪽(`/{customerId}`)이 남아야 구분이 된다. */
  it('공통 라우트 접두사를 그룹 머리로 올린다', () => {
    expect(commonRoutePrefix(['/api/v1/reports/daily', '/api/v1/reports/weekly'])).toBe('/api/v1/reports')
    // 한 조각은 남긴다. 다 먹으면 라우트가 통째로 사라진다.
    expect(commonRoutePrefix(['/api/orders', '/api/orders'])).toBe('/api')
    expect(commonRoutePrefix(['/api/orders', '/health'])).toBe('')
    expect(commonRoutePrefix(['/api/orders'])).toBe('')
  })

  /** 정렬은 화면에 보이는 순서를 정한다. 동사 정렬은 알파벳순이 아니라 읽는 순서다. */
  it('정렬 기준을 바꾸면 항목 순서가 바뀐다', () => {
    const items = [
      entry({ route: '/b', httpMethod: 'DELETE', endpointId: 'DELETE /b' }),
      entry({ route: '/a', httpMethod: 'POST', endpointId: 'POST /a' }),
    ]
    const options = { groupBy: 'controller' as const, classIdOfMethod: () => 'C', unknownLabel: unknown }

    expect(groupEntryPoints(items, { ...options, sort: 'route' })[0].items.map((item) => item.route))
      .toEqual(['/a', '/b'])
    expect(groupEntryPoints(items, { ...options, sort: 'method' })[0].items.map((item) => item.httpMethod))
      .toEqual(['POST', 'DELETE'])
  })
})
