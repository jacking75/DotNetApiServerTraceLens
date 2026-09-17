import type { ContextIntent, SelectionKind } from '../types/context'
import type { ViewMode } from '../types/graph'

/**
 * 실무 절차(레시피)를 구조화 데이터로 둔다.
 * 앱 안내와 한·영 매뉴얼이 같은 절차 ID(`recipeId`)를 가리키게 해서 설명이 갈라지지 않게 한다.
 * 번역 문구는 여기 두지 않고 i18n 키로만 참조한다.
 */
export interface Recipe {
  id: 'understand-api' | 'plan-change' | 'investigate-data' | 'review-ai-change'
  /** 이 절차를 시작할 보기. */
  view: ViewMode
  /** 이 절차에 맞는 선택 종류와 문맥 목적. */
  selectionKind: SelectionKind
  intent: ContextIntent
  titleKo: string
  titleEn: string
  stepsKo: string[]
  stepsEn: string[]
  /** 매뉴얼에서 이 절차를 설명하는 앵커. 앱의 "설명서에서 보기"가 이 위치로 연다. */
  manualAnchor: string
  /** AI에 그대로 붙여 쓸 수 있는 요청 예시. */
  promptKo: string
  promptEn: string
  verifyKo: string
  verifyEn: string
}

export const recipes: readonly Recipe[] = [
  {
    id: 'understand-api',
    manualAnchor: 'recipe-understand-api',
    view: 'callflow',
    selectionKind: 'Endpoint',
    intent: 'Explain',
    titleKo: '처음 보는 API를 이해한다',
    titleEn: 'Understand an API you have never seen',
    stepsKo: [
      '호출 흐름에서 진입점을 하나 고른다.',
      '대표 호출 경로를 따라가며 각 단계의 파일·줄을 확인한다.',
      '문맥을 복사해 AI에 전달한다.',
    ],
    stepsEn: [
      'Pick one entry point in the call flow view.',
      'Follow the representative path and check the file and line of each step.',
      'Copy the context and hand it to your AI tool.',
    ],
    promptKo:
      '이 API의 입력부터 저장까지를 근거 ID와 함께 설명한다. 정적으로 확인할 수 없는 처리도 명시한다.',
    promptEn:
      'Explain this API from input to storage, citing the evidence ids. Also state what cannot be confirmed statically.',
    verifyKo: '설명의 사실 주장이 소스와 대응하는지 확인한다.',
    verifyEn: 'Check that each factual claim maps to the source.',
  },
  {
    id: 'plan-change',
    manualAnchor: 'recipe-plan-change',
    view: 'callflow',
    selectionKind: 'Node',
    intent: 'PlanChange',
    titleKo: '기능 추가를 계획한다',
    titleEn: 'Plan a change',
    stepsKo: [
      '바꾸려는 메서드를 고른다.',
      '"변경 영향"에서 영향받는 API 목록을 확인한다.',
      '문맥을 만들어 AI에 전달한다.',
    ],
    stepsEn: [
      'Select the method you plan to change.',
      'Check the affected APIs under "Change impact".',
      'Build the context and hand it to your AI tool.',
    ],
    promptKo:
      '기존 책임 분리를 유지하면서 변경 위치, 영향 API, 필요한 테스트를 제안한다.',
    promptEn:
      'Propose where to change, which APIs are affected, and which tests are needed, keeping the current separation of concerns.',
    verifyKo: '변경 위치와 테스트 후보의 근거를 확인한다.',
    verifyEn: 'Check the evidence behind the suggested locations and test candidates.',
  },
  {
    id: 'investigate-data',
    manualAnchor: 'recipe-investigate-data',
    view: 'data',
    selectionKind: 'WritePath',
    intent: 'InvestigateData',
    titleKo: 'DB·캐시 불일치를 조사한다',
    titleEn: 'Investigate a data or cache mismatch',
    stepsKo: [
      'DB · 캐시 보기의 보상 처리에서 쓰기 경로를 고른다.',
      '각 단계의 보상 판정과 근거를 읽는다.',
      '점검 항목을 골라 문맥을 만든다.',
    ],
    stepsEn: [
      'Pick a write path under Data & cache → compensation.',
      'Read each step\'s compensation verdict and its evidence.',
      'Select a check and build the context.',
    ],
    promptKo:
      '중간 실패로 남는 갱신을 확인하고, 추정을 구분하여 개선안을 제시한다.',
    promptEn:
      'Identify updates left behind by a mid-way failure, separate the estimated parts, and propose a fix.',
    verifyKo: '실제 코드와 실패 경로를 확인한다.',
    verifyEn: 'Check the real code and the failure path.',
  },
  {
    id: 'review-ai-change',
    manualAnchor: 'recipe-review-ai-change',
    view: 'callflow',
    selectionKind: 'Node',
    intent: 'ReviewChange',
    titleKo: 'AI 수정을 검토한다',
    titleEn: 'Review a change made by AI',
    stepsKo: [
      '수정 전에 "변경 전으로 저장"을 눌러 기준을 만든다.',
      '외부 AI로 코드를 수정한다.',
      '"재분석하고 비교"를 눌러 차이를 본다.',
    ],
    stepsEn: [
      'Press "Save as before" to record a baseline.',
      'Let your AI tool change the code.',
      'Press "Re-analyze and compare" to see the difference.',
    ],
    promptKo:
      '이 차이에서 의도하지 않은 경로 변화, 남은 점검, 검증 부족을 확인한다.',
    promptEn:
      'From this diff, point out unintended path changes, remaining checks, and gaps in verification.',
    verifyKo: '분석 차이와 로컬 테스트 결과를 따로 확인한다. 차이 없음은 안전을 뜻하지 않는다.',
    verifyEn: 'Check the analysis diff and your local test results separately. "No diff" is not "safe".',
  },
]

export function findRecipe(id: Recipe['id']): Recipe | undefined {
  return recipes.find((recipe) => recipe.id === id)
}
