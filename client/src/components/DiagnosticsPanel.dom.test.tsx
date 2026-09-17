/**
 * @vitest-environment jsdom
 */
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { I18nProvider } from '../i18n/I18nProvider'
import type { AnalysisDiagnostics } from '../types/graph'
import { DiagnosticsPanel } from './DiagnosticsPanel'

const diagnostics: AnalysisDiagnostics = {
  totalProjects: 3,
  loadedProjects: 2,
  projectsWithCompileErrors: 1,
  projects: [
    { name: 'Ok.Api', path: 'C:\\Work\\Ok.Api.csproj', loaded: true, errorCount: 0, sampleErrors: [] },
    {
      name: 'Broken.Core',
      path: 'C:\\Work\\Broken.Core.csproj',
      loaded: true,
      errorCount: 2,
      sampleErrors: ['CS1002: ; 이 필요합니다.'],
    },
    { name: 'Missing.Tool', path: 'C:\\Work\\Missing.Tool.csproj', loaded: false, errorCount: 0, sampleErrors: [] },
  ],
  calls: {
    inspected: 28,
    resolved: 28,
    direct: 20,
    diRegistration: 6,
    singleImplementation: 2,
    ambiguous: 0,
    unresolved: 0,
    external: 1168,
    resolutionRate: 1,
  },
  excludedNamespacePrefixes: [],
  maxCallDepth: 6,
  depthLimitReached: false,
  analysisScope: 'solution',
  limitations: ['호출 근거는 엣지당 3개까지만 남긴다.'],
  changedDuringAnalysis: false,
  changedDuringAnalysisFiles: [],
  entryPointNotes: [],
}

/**
 * 분석기는 프로젝트 로드 결과·한계·외부 호출 수를 이미 기록하지만 이전에는 어디에도 그리지 않았다.
 * 이 테스트는 "그래프를 어디까지 믿을 수 있나"를 답하는 세 가지가 실제로 화면에 나오는지 본다.
 */
describe('DiagnosticsPanel', () => {
  beforeEach(() => window.localStorage.setItem('tracelens.lang', 'ko'))
  afterEach(cleanup)

  it('컴파일 오류 프로젝트와 첫 오류, 한계, 외부 호출 문장을 보여 준다', () => {
    render(
      <I18nProvider>
        <DiagnosticsPanel
          diagnostics={diagnostics}
          minimalApiCount={4}
          entryPointCount={8}
          onClose={() => {}}
        />
      </I18nProvider>,
    )

    expect(screen.getByText('Broken.Core')).toBeTruthy()
    expect(screen.getByText('CS1002: ; 이 필요합니다.')).toBeTruthy()
    expect(screen.getByText('Missing.Tool')).toBeTruthy()
    expect(screen.getByText('호출 근거는 엣지당 3개까지만 남긴다.')).toBeTruthy()
    expect(screen.getByText(/솔루션 밖 호출 1168건은 해석률 분모에서 뺐다/)).toBeTruthy()
  })

  it('진단이 없는 옛 그래프에는 사유를 말한다', () => {
    render(
      <I18nProvider>
        <DiagnosticsPanel diagnostics={null} minimalApiCount={0} entryPointCount={0} onClose={() => {}} />
      </I18nProvider>,
    )

    expect(screen.getByText(/진단 정보가 없다/)).toBeTruthy()
  })
})
