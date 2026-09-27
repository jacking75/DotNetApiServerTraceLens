/** @vitest-environment jsdom */
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import * as api from '../api/client'
import { I18nProvider, useI18n } from '../i18n/I18nProvider'
import type { AnalysisJob, AnalysisJobEvent } from '../types/context'
import type { AnalyzeSummary, GraphDocument } from '../types/graph'
import { useAnalysisJob } from './useAnalysisJob'

vi.mock('../api/client', async (original) => ({
  ...await original<typeof import('../api/client')>(),
  listAnalysisJobs: vi.fn(), startAnalysisJob: vi.fn(), watchAnalysisJob: vi.fn(),
  cancelAnalysisJob: vi.fn(), analyzeTarget: vi.fn(),
}))
const job: AnalysisJob = { id: 'job', targetPath: 'Sample.csproj', state: 'Running', startedAt: new Date().toISOString(), error: null, finishedAt: null }
const event = (kind: AnalysisJobEvent['kind']): AnalysisJobEvent => ({ kind, stage: '', message: 'message', count: null, result: null })
beforeEach(() => {
  vi.resetAllMocks()
  localStorage.setItem('tracelens.lang', 'ko')
  vi.mocked(api.listAnalysisJobs).mockResolvedValue([])
  vi.mocked(api.startAnalysisJob).mockResolvedValue(job)
  vi.mocked(api.watchAnalysisJob).mockImplementation(async (_id, receive) => { receive(event('done')) })
})
afterEach(cleanup)

function setup() {
  const options = {
    solutionPath: job.targetPath,
    loadGraphData: vi.fn(async () => ({} as GraphDocument)),
    summarizeFrom: vi.fn(async () => ({} as AnalyzeSummary)),
    applyAnalyzedGraph: vi.fn(), setError: vi.fn(), setErrorId: vi.fn(), setToast: vi.fn(),
    setTargetPicked: vi.fn(), rememberTarget: vi.fn(), setSolutionPath: vi.fn(),
    describeError: (_reason: unknown, summary: string) => ({ summary, detail: null, code: null }),
  }
  const hook = renderHook(() => useAnalysisJob({ ...options, t: useI18n().t }), { wrapper: I18nProvider })
  return { ...hook, options }
}

it.each(['error', 'canceled'] as const)('%s 이후에는 성공을 반환하거나 이전 그래프를 다시 읽지 않는다', async (kind) => {
  vi.mocked(api.watchAnalysisJob).mockImplementation(async (_id, receive) => { receive(event(kind)) })
  const { result, options } = setup()
  let succeeded: boolean | undefined
  await act(async () => { succeeded = await result.current.analyze() })
  expect(succeeded).toBe(false)
  expect(options.loadGraphData).not.toHaveBeenCalled()
  expect(result.current.busy).toBe(false)
})

it('완료 이벤트 없이 연결이 끝나면 복구 안내를 표시한다', async () => {
  vi.mocked(api.watchAnalysisJob).mockResolvedValue()
  const { result, options } = setup()
  await act(async () => { expect(await result.current.analyze()).toBe(false) })
  expect(options.loadGraphData).not.toHaveBeenCalled()
  expect(options.setError).toHaveBeenLastCalledWith(expect.objectContaining({ code: 'NETWORK' }))
})

it('완료 후 그래프를 읽고 적용했을 때만 성공을 반환한다', async () => {
  const { result, options } = setup()
  await act(async () => { expect(await result.current.analyze()).toBe(true) })
  expect(options.applyAnalyzedGraph).toHaveBeenCalledOnce()
  expect(options.rememberTarget).toHaveBeenCalledWith(job.targetPath)
})

it('작업 시작 API가 없을 때만 구버전 API로 전환한다', async () => {
  vi.mocked(api.startAnalysisJob).mockRejectedValue(new api.ApiError('missing', 'NOT_FOUND', 404))
  vi.mocked(api.analyzeTarget).mockResolvedValue({} as AnalyzeSummary)
  const { result } = setup()
  await act(async () => { expect(await result.current.analyze()).toBe(true) })
  expect(api.analyzeTarget).toHaveBeenCalledOnce()
})

it('분석 완료 후 그래프 조회가 404여도 분석을 다시 시작하지 않는다', async () => {
  const { result, options } = setup()
  options.loadGraphData.mockRejectedValue(new api.ApiError('missing graph', 'NOT_FOUND', 404))
  await act(async () => { expect(await result.current.analyze()).toBe(false) })
  expect(api.analyzeTarget).not.toHaveBeenCalled()
})

it('연속 실행을 막고 취소 요청 실패를 화면에 알린다', async () => {
  let finish!: () => void
  vi.mocked(api.watchAnalysisJob).mockImplementation(() => new Promise<void>((resolve) => { finish = resolve }))
  vi.mocked(api.cancelAnalysisJob).mockRejectedValue(new Error('offline'))
  const { result, options } = setup()
  let pending!: Promise<boolean>
  await act(async () => { pending = result.current.analyze() })
  await waitFor(() => expect(result.current.jobId).toBe(job.id))
  await act(async () => {
    expect(await result.current.analyze()).toBe(false)
    await result.current.cancelAnalysis()
  })
  expect(api.startAnalysisJob).toHaveBeenCalledOnce()
  expect(options.setError).toHaveBeenLastCalledWith(expect.objectContaining({ summary: expect.stringContaining('취소 요청에 실패') }))
  await act(async () => { finish(); await pending })
})
