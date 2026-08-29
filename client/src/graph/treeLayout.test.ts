import { describe, expect, it } from 'vitest'
import type { ClassRelation } from './relations'
import { assignLayers, buildLayers, countCrossings, layoutTree, reduceCrossings } from './treeLayout'

const node = (id: string) => ({ id, w: 200, h: 60 })
const depend = (from: string, to: string): ClassRelation => ({ from, to, type: 'depend' })
const inherit = (from: string, to: string): ClassRelation => ({ from, to, type: 'inherit' })

describe('층 나누기', () => {
  it('최장 경로 기준으로 층을 매긴다', () => {
    const layers = assignLayers(['A', 'B', 'C', 'D'], [['A', 'B'], ['B', 'C'], ['A', 'C'], ['C', 'D']])

    expect(layers.get('A')).toBe(0)
    expect(layers.get('B')).toBe(1)
    // A→C도 있지만 A→B→C가 더 길므로 2층이다.
    expect(layers.get('C')).toBe(2)
    expect(layers.get('D')).toBe(3)
  })

  it('순환이 있어도 모든 노드에 층을 준다', () => {
    const layers = assignLayers(['A', 'B', 'C'], [['A', 'B'], ['B', 'C'], ['C', 'B']])

    expect(layers.size).toBe(3)
    expect([...layers.values()].every((value) => Number.isFinite(value))).toBe(true)
  })

  it('상속은 방향을 뒤집어 상위 타입을 앞 층에 둔다', () => {
    const { rows } = buildLayers([node('Base'), node('Derived')], [inherit('Derived', 'Base')], 'LR')

    expect(rows[0].map((item) => item.key)).toEqual(['Base'])
    expect(rows[1].map((item) => item.key)).toEqual(['Derived'])
  })

  it('관계가 없는 클래스는 마지막 층에 모은다', () => {
    const { rows } = buildLayers(
      [node('A'), node('B'), node('Lonely')],
      [depend('A', 'B')],
      'LR')

    expect(rows).toHaveLength(3)
    expect(rows[2].map((item) => item.key)).toEqual(['Lonely'])
  })
})

describe('더미 삽입', () => {
  it('층을 2개 이상 건너뛰는 선에는 중간 층마다 경유점을 넣는다', () => {
    const { rows, chains } = buildLayers(
      [node('A'), node('B'), node('C')],
      [depend('A', 'B'), depend('B', 'C'), depend('A', 'C')],
      'LR')

    const longChain = chains.find((chain) => chain.keys[0] === 'A' && chain.keys[chain.keys.length - 1] === 'C')
    expect(longChain?.keys).toHaveLength(3)
    // 더미는 실제 박스가 아니라 1층에 끼어 있어야 한다.
    expect(rows[1].filter((item) => item.dummy)).toHaveLength(1)
  })

  it('바로 옆 층으로 가는 선에는 경유점을 넣지 않는다', () => {
    const { rows, chains } = buildLayers([node('A'), node('B')], [depend('A', 'B')], 'LR')

    expect(chains[0].keys).toEqual(['A', 'B'])
    expect(rows.flat().some((item) => item.dummy)).toBe(false)
  })
})

describe('교차 줄이기', () => {
  it('바리센터 정렬이 교차 수를 줄인다', () => {
    // 위층 A,B,C ↔ 아래층 X,Y,Z를 일부러 엇갈리게 이어 둔다.
    const nodes = ['A', 'B', 'C', 'X', 'Y', 'Z'].map(node)
    const relations = [depend('A', 'Z'), depend('B', 'Y'), depend('C', 'X'), depend('A', 'Y')]
    const { rows, chains } = buildLayers(nodes, relations, 'LR')
    // 선언 순서 그대로면 선이 서로 엇갈린다.
    const before = countCrossings(rows, chains)

    reduceCrossings(rows, chains)
    const after = countCrossings(rows, chains)

    expect(before).toBeGreaterThan(0)
    expect(after).toBeLessThan(before)
  })
})

describe('트리 배치', () => {
  it('층은 진행축으로 쌓이고 박스는 겹치지 않는다', () => {
    const layout = layoutTree(
      [node('A'), node('B'), node('C')],
      [depend('A', 'B'), depend('A', 'C')],
      { direction: 'LR' })

    const a = layout.boxes.get('A')!
    const b = layout.boxes.get('B')!
    const c = layout.boxes.get('C')!
    expect(a.x).toBeLessThan(b.x)
    expect(b.x).toBe(c.x)
    // 같은 층 안에서는 교차축으로 벌어져야 한다.
    expect(Math.abs(b.y - c.y)).toBeGreaterThanOrEqual(b.h)
    expect(layout.size.width).toBeGreaterThan(a.w)
  })

  it('방향을 바꾸면 진행축도 바뀐다', () => {
    const layout = layoutTree([node('A'), node('B')], [depend('A', 'B')], { direction: 'TB' })

    expect(layout.boxes.get('A')!.y).toBeLessThan(layout.boxes.get('B')!.y)
    expect(layout.boxes.get('A')!.x).toBe(layout.boxes.get('B')!.x)
  })

  it('순환하는 선은 되돌아가는 선으로 표시한다', () => {
    const layout = layoutTree([node('A'), node('B')], [depend('A', 'B'), depend('B', 'A')], { direction: 'LR' })

    expect(layout.paths.filter((path) => path.backward)).toHaveLength(1)
    expect(layout.paths.every((path) => path.d.startsWith('M '))).toBe(true)
  })

  it('빈 입력에는 빈 배치를 돌려준다', () => {
    const layout = layoutTree([], [], { direction: 'LR' })

    expect(layout.boxes.size).toBe(0)
    expect(layout.size).toEqual({ width: 0, height: 0 })
  })
})
