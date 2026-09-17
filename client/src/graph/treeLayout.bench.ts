import { bench, describe } from 'vitest'
import type { ClassRelation } from './relations'
import { cullToViewport, layoutTree, type LayoutNode } from './treeLayout'

/**
 * 클래스 다이어그램 배치 비용을 재는 측정 스크립트다(`npm run measure:layout`).
 * 성능 가드(임계값 assert)를 두지 않는 이유: 기계마다 값이 달라서 CI에서 깜빡이는 실패만 늘어난다.
 * 여기서 나온 수치는 CHANGELOG에 "측정 조건과 함께" 적는다.
 */
function buildSolution(classCount: number) {
  const nodes: LayoutNode[] = []
  const relations: ClassRelation[] = []
  // 실제 솔루션 비율을 흉내 낸다: 컨트롤러 1 : 서비스 3 : 저장소 2 : 모델 4.
  for (let index = 0; index < classCount; index += 1) {
    const kind = index % 10
    nodes.push({
      id: `N${index}`,
      w: kind === 0 ? 234 : 200,
      // 멤버를 펼친 박스 높이. 접으면 44px라 면적이 훨씬 줄어든다.
      h: kind >= 6 ? 96 : 180,
    })
  }

  for (let index = 0; index < classCount; index += 1) {
    const kind = index % 10
    if (kind === 0 && index + 1 < classCount) {
      relations.push({ from: `N${index}`, to: `N${index + 1}`, type: 'depend' })
    }
    if (kind >= 1 && kind <= 3 && index + 4 < classCount) {
      relations.push({ from: `N${index}`, to: `N${index + 4}`, type: 'assoc' })
    }
    if (kind >= 6 && index >= 10) {
      relations.push({ from: `N${index}`, to: `N${index - 10}`, type: 'inherit' })
    }
  }

  return { nodes, relations }
}

const small = buildSolution(150)
const real = buildSolution(564)

describe('layoutTree', () => {
  bench('클래스 150개 (밀도 손잡이 안내가 뜨는 경계)', () => {
    layoutTree(small.nodes, small.relations)
  })

  bench('클래스 564개 (실제 프로젝트 규모)', () => {
    layoutTree(real.nodes, real.relations)
  })
})

describe('cullToViewport', () => {
  const layout = layoutTree(real.nodes, real.relations)
  const viewport = { left: 0, top: 0, width: 1600, height: 900 }

  bench('클래스 564개에서 보이는 박스만 고르기', () => {
    cullToViewport(real.nodes, layout.boxes, viewport)
  })
})
