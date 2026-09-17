import type { TranslationKey } from './ko'
import type { SelectionKind } from '../types/context'
import type { AccessOperation, CallConfidence, CallResolution, FreshnessStatus } from '../types/graph'

/**
 * 서버가 내려보내는 enum 원문(`Direct`·`Certain`·`fresh`…)을 화면 문구로 바꾸는 공용 매핑이다.
 * 상세·문맥 미리보기·영향·DB 표가 같은 함수를 써야 같은 값이 화면마다 다르게 보이지 않는다.
 * 여기 있는 것은 모두 **번역 대상**이다. 심볼·라우트·파일 경로는 여기에 넣지 않는다.
 */

const resolutionKeys: Record<CallResolution, TranslationKey> = {
  Direct: 'detail.resolution.direct',
  DiRegistration: 'detail.resolution.diRegistration',
  SingleImplementation: 'detail.resolution.singleImplementation',
  Ambiguous: 'detail.resolution.ambiguous',
  Unresolved: 'detail.resolution.unresolved',
  External: 'detail.resolution.external',
}

const confidenceKeys: Record<CallConfidence, TranslationKey> = {
  Certain: 'detail.confidence.certain',
  Estimated: 'detail.confidence.estimated',
}

const freshnessKeys: Record<FreshnessStatus, TranslationKey> = {
  fresh: 'freshness.fresh',
  stale: 'freshness.stale',
  unknown: 'freshness.unknown',
}

const operationKeys: Record<AccessOperation, TranslationKey> = {
  Read: 'op.read',
  Write: 'op.write',
  Delete: 'op.delete',
  Other: 'op.other',
  Unknown: 'op.unknown',
}

const selectionKindKeys: Record<SelectionKind, TranslationKey> = {
  Node: 'selection.method',
  Class: 'selection.class',
  Namespace: 'selection.namespace',
  Endpoint: 'selection.endpoint',
  Finding: 'selection.finding',
  WritePath: 'selection.writePath',
  MiddlewareStep: 'selection.middlewareStep',
  StartupStep: 'selection.startupStep',
}

type Translate = (key: TranslationKey, values?: Record<string, string | number>) => string

/** 알 수 없는 값은 원문을 그대로 보여 준다(조용히 지우지 않는다). */
function lookup<T extends string>(map: Record<T, TranslationKey>, value: string, t: Translate): string {
  const key = (map as Record<string, TranslationKey | undefined>)[value]
  return key ? t(key) : value
}

export const resolutionLabel = (value: string, t: Translate) => lookup(resolutionKeys, value, t)
export const confidenceLabel = (value: string, t: Translate) => lookup(confidenceKeys, value, t)
export const freshnessLabel = (value: string, t: Translate) => lookup(freshnessKeys, value, t)
export const operationLabel = (value: string, t: Translate) => lookup(operationKeys, value, t)
export const selectionKindLabel = (value: string, t: Translate) => lookup(selectionKindKeys, value, t)
