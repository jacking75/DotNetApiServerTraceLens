import { describe, expect, it } from 'vitest'
import { isStructuralChange, parse, serialize, type UrlState } from './urlState'

const state: UrlState = {
  view: 'callflow',
  entry: 'Shop|POST|/api/orders|Create',
  focus: ['Shop.OrderService.CreateAsync(int)', 'Shop.OrderRepository.InsertAsync(int)'],
  selection: { type: 'method', key: 'Shop.OrderRepository.InsertAsync(int)' },
  sub: null,
  flowEntry: null,
  depth: null,
  classMap: null,
  direction: null,
}

const empty: UrlState = {
  view: 'classmap',
  entry: null,
  focus: [],
  selection: null,
  sub: null,
  flowEntry: null,
  depth: null,
  classMap: null,
  direction: null,
}

describe('urlState', () => {
  /** URL을 복사해 새 탭에 붙여 넣으면 같은 화면이어야 한다. 왕복이 깨지면 그 약속이 깨진다. */
  it('직렬화한 값을 그대로 되돌린다', () => {
    expect(parse(serialize(state))).toEqual({
      view: state.view,
      entry: state.entry,
      focus: state.focus,
      selection: state.selection,
    })
  })

  /** 파이프·슬래시가 든 엔드포인트 ID가 해시 구분자와 부딪히면 안 된다. */
  it('구분자가 든 ID도 되살린다', () => {
    const hash = serialize({ ...state, entry: 'a&b=c|d', focus: ['x,y'], selection: null })

    const parsed = parse(hash)

    expect(parsed.entry).toBe('a&b=c|d')
    expect(parsed.focus).toEqual(['x,y'])
    expect(parsed.selection).toBeUndefined()
  })

  it('빈 값은 해시에 넣지 않는다', () => {
    expect(serialize(empty)).toBe('#view=classmap')
  })

  /** 옛 링크나 손으로 고친 URL 때문에 화면이 깨지면 안 된다. */
  it('모르는 키와 값은 무시한다', () => {
    const parsed = parse('#view=nope&unknown=1&sel=bogus:x&entry=keep')

    expect(parsed.view).toBeUndefined()
    expect(parsed.selection).toBeUndefined()
    expect(parsed.entry).toBe('keep')
  })

  it('빈 해시는 빈 상태다', () => {
    expect(parse('')).toEqual({})
    expect(parse('#')).toEqual({})
  })

  /** T45: 새 탭에서 열려면 하위 탭·전체 흐름도 진입점·깊이·클래스 토글도 해시에 있어야 한다. */
  it('하위 탭·흐름도 진입점·깊이·클래스 토글을 담는다', () => {
    const hash = serialize({
      ...empty,
      view: 'data',
      sub: 'comp',
      flowEntry: 'Shop.OrdersController.Create()',
      depth: 5,
      classMap: { showFields: true, showMethods: false, focusOnly: true,
        relations: { inherit: true, implement: false, assoc: true, depend: false } },
      direction: 'up',
    })

    const parsed = parse(hash)

    expect(parsed.sub).toBe('comp')
    expect(parsed.flowEntry).toBe('Shop.OrdersController.Create()')
    expect(parsed.depth).toBe(5)
    expect(parsed.direction).toBe('up')
    expect(parsed.classMap).toEqual({
      showFields: true,
      showMethods: false,
      focusOnly: true,
      relations: { inherit: true, implement: false, assoc: true, depend: false },
    })
  })

  /**
   * 히스토리 규칙: 선택만 바뀌면 덮어쓰고 보기·진입점·포커스·하위 탭이 바뀌면 쌓는다.
   * 이 규칙이 깨지면 노드 20개를 눌러 본 뒤 뒤로 가기를 20번 눌러야 한다.
   */
  it('선택만 바뀌면 구조 변경이 아니다', () => {
    const previous = parse(serialize(state))

    expect(isStructuralChange(previous, parse(serialize({ ...state, selection: { type: 'class', key: 'X' } }))))
      .toBe(false)
    expect(isStructuralChange(previous, parse(serialize({ ...state, view: 'classmap' })))).toBe(true)
    expect(isStructuralChange(previous, parse(serialize({ ...state, focus: [] })))).toBe(true)
    expect(isStructuralChange(previous, parse(serialize({ ...state, view: 'data', sub: 'map' })))).toBe(true)
    expect(isStructuralChange(null, previous)).toBe(true)
  })
})
