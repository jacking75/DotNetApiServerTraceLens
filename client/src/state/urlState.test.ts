import { describe, expect, it } from 'vitest'
import { parse, serialize, type UrlState } from './urlState'

const state: UrlState = {
  view: 'callflow',
  entry: 'Shop|POST|/api/orders|Create',
  focus: ['Shop.OrderService.CreateAsync(int)', 'Shop.OrderRepository.InsertAsync(int)'],
  selection: { type: 'method', key: 'Shop.OrderRepository.InsertAsync(int)' },
}

describe('urlState', () => {
  /** URL을 복사해 새 탭에 붙여 넣으면 같은 화면이어야 한다. 왕복이 깨지면 그 약속이 깨진다. */
  it('직렬화한 값을 그대로 되돌린다', () => {
    expect(parse(serialize(state))).toEqual(state)
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
    expect(serialize({ view: 'classmap', entry: null, focus: [], selection: null })).toBe('#view=classmap')
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
})
