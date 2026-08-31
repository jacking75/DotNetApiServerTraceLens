import type { GraphDocument, StartupFlowDocument, StartupLifetime, StartupStep } from '../types/graph'

export type StartupGroup = 'svc' | 'mw'

/** 트리 안 위치(인덱스 경로)로 단계 키를 만든다. 예: "svc:3" → 서비스 등록 4번째, "svc:3.0" → 그 확장 메서드의 첫 하위 단계. */
export function startupStepKey(group: StartupGroup, path: number[]): string {
  return `${group}:${path.join('.')}`
}

/** 단계 키로 실제 단계를 찾는다. 재분석 후 선택을 유지할지 판단하거나(D-7), 상세 패널에서 쓴다. */
export function findStartupStep(graph: GraphDocument, key: string): StartupStep | undefined {
  const flow = graph.startupFlow
  if (!flow) return undefined

  const separatorIndex = key.indexOf(':')
  if (separatorIndex < 0) return undefined
  const group = key.slice(0, separatorIndex)
  const pathPart = key.slice(separatorIndex + 1)
  const roots = group === 'svc' ? flow.serviceSteps : group === 'mw' ? flow.middlewareSteps : null
  if (!roots || !pathPart) return undefined

  let steps: StartupStep[] = roots
  let step: StartupStep | undefined
  for (const segment of pathPart.split('.')) {
    const index = Number(segment)
    step = steps[index]
    if (!step) return undefined
    steps = step.children
  }

  return step
}

export interface StartupLifetimeCounts {
  scoped: number
  singleton: number
  transient: number
}

/** 좌측 패널 요약용. 등록 단계를 수명별로 센다(하위 단계까지 재귀로 훑는다). */
export function countByLifetime(steps: StartupStep[]): StartupLifetimeCounts {
  const counts: StartupLifetimeCounts = { scoped: 0, singleton: 0, transient: 0 }
  const visit = (list: StartupStep[]) => {
    for (const step of list) {
      if (step.lifetime) counts[lifetimeKey(step.lifetime)] += 1
      if (step.children.length > 0) visit(step.children)
    }
  }

  visit(steps)
  return counts
}

function lifetimeKey(lifetime: StartupLifetime): keyof StartupLifetimeCounts {
  if (lifetime === 'Scoped') return 'scoped'
  if (lifetime === 'Singleton') return 'singleton'
  return 'transient'
}

/** 시작 흐름 전체(서비스+미들웨어)의 단계 수를 재귀로 센다. */
export function countSteps(flow: StartupFlowDocument): number {
  const visit = (list: StartupStep[]): number =>
    list.reduce((sum, step) => sum + 1 + visit(step.children), 0)
  return visit(flow.serviceSteps) + visit(flow.middlewareSteps)
}
