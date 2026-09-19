import type { EntryPoint, GraphDocument } from './graph'

/**
 * 테스트에서 v7 필수 필드를 매번 채우지 않도록 두는 기본값이다.
 * 화면 코드가 쓰는 값이 아니라 테스트 픽스처 전용이다.
 */
export const nodeDefaults = {
  callable: 'Method',
  stableKey: '',
  signatureFingerprint: '',
  bodyFingerprint: '',
} as const

export function entryPoint(nodeId: string, httpMethod: string, route: string): EntryPoint {
  return {
    nodeId,
    httpMethod,
    route,
    endpointId: `${httpMethod} ${route} :: ${nodeId}`,
    normalizedRoute: route.toLowerCase(),
    source: 'Controller',
    handlerKind: 'Method',
    confidence: 'Certain',
    registrationFile: '',
    registrationLine: 0,
    handlerDisplayName: nodeId,
    projectKey: '',
    notes: [],
    groupKey: '',
    hiddenFromApiExplorer: false,
    authorization: null,
  }
}

export const graphDefaults: Pick<
  GraphDocument,
  | 'snapshotId'
  | 'inputFingerprint'
  | 'manifest'
  | 'diagnostics'
  | 'callSites'
  | 'analyzerVersion'
  | 'rulesVersion'
  | 'startupFlows'
  | 'middlewarePipelines'
> = {
  snapshotId: 'test-snapshot',
  inputFingerprint: '',
  manifest: null,
  diagnostics: null,
  callSites: [],
  analyzerVersion: '',
  rulesVersion: '',
  startupFlows: [],
  middlewarePipelines: [],
}
