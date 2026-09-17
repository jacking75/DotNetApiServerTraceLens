import { describe, expect, it } from 'vitest'
import { glossaryTerms } from '../components/HelpDialog'
import { en } from './en'
import { ko } from './ko'

describe('i18n 사전', () => {
  it('영어 사전이 한국어 사전의 키를 모두 채운다', () => {
    const missing = Object.keys(ko).filter((key) => !(key in en))
    expect(missing).toEqual([])
  })

  it('영어 사전에 한국어 사전에 없는 키가 없다', () => {
    const extra = Object.keys(en).filter((key) => !(key in ko))
    expect(extra).toEqual([])
  })

  it('영어 사전의 값이 비어 있지 않다', () => {
    const empty = Object.entries(en).filter(([, value]) => value.trim().length === 0)
    expect(empty).toEqual([])
  })

  /** `{name}` 자리표시자가 두 언어에서 같아야 값이 뒤바뀌지 않는다. */
  it('두 사전의 자리표시자가 같다', () => {
    const placeholders = (value: string) => (value.match(/\{\w+\}/g) ?? []).slice().sort()
    const mismatched = Object.keys(ko).filter((key) => {
      const left = placeholders(ko[key as keyof typeof ko])
      const right = placeholders(en[key as keyof typeof en])
      return left.join(',') !== right.join(',')
    })
    expect(mismatched).toEqual([])
  })

  /** 용어집 항목은 두 언어 모두에 제목·본문이 있어야 `?` 링크가 빈 칸을 열지 않는다. */
  it('용어집 12항목의 한·영 제목·본문이 모두 있다', () => {
    const missing = glossaryTerms.flatMap((term) => [`glossary.${term}.title`, `glossary.${term}.body`])
      .filter((key) => !(key in ko) || !(key in en))
    expect(glossaryTerms.length).toBe(12)
    expect(missing).toEqual([])
  })

  /**
   * 영어로 전환했을 때 한국어가 남아 있으면 안 된다.
   * 다만 언어 이름(한국어)처럼 일부러 원어를 쓰는 값은 예외로 둔다.
   */
  it('영어 사전에 번역이 빠진 한국어 문장이 없다', () => {
    const allowed = new Set(['lang.ko'])
    const korean = /[가-힣]/
    const leftovers = Object.entries(en)
      .filter(([key, value]) => !allowed.has(key) && korean.test(value))
      .map(([key]) => key)
    expect(leftovers).toEqual([])
  })
})
