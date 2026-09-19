import { useCallback, useEffect, useState } from 'react'
import {
  ApiError,
  analyzeTarget,
  cancelAnalysisJob,
  listAnalysisJobs,
  startAnalysisJob,
  watchAnalysisJob,
} from '../api/client'
import { translateError, type I18n } from '../i18n/I18nProvider'
import type { AnalysisJob } from '../types/context'
import type { AnalyzeSummary, GraphDocument } from '../types/graph'

/** 배너가 쓰는 실패 한 벌. 원문은 버리지 않고 `detail`에 남긴다. */
export interface AppError {
  summary: string
  detail: string | null
  code: string | null
}

/** 분석이 오래 걸린다고 볼 시간. 이 뒤에는 "기다리는 것 말고 할 수 있는 일"을 알려 준다. */
const longRunningHintMs = 60_000

export interface UseAnalysisJobOptions {
  /** 분석 대상 경로. `analyze()`에 인자를 주지 않으면 이 값을 쓴다. */
  solutionPath: string
  loadGraphData: () => Promise<GraphDocument>
  applyAnalyzedGraph: (graph: GraphDocument, summary: AnalyzeSummary) => void
  summarizeFrom: (graph: GraphDocument) => Promise<AnalyzeSummary>
  describeError: (reason: unknown, fallback: string) => AppError
  setError: (error: AppError | null) => void
  setErrorId: (errorId: string | null) => void
  setToast: (message: string) => void
  /** 분석을 시작하면 "대상을 골랐다" 안내를 접는다. */
  setTargetPicked: (picked: boolean) => void
  /** 성공한 대상을 최근 목록에 올린다. */
  rememberTarget: (target: string) => void
  /** 서버에서 돌고 있던 작업에 다시 붙었을 때 대상 칸을 맞춘다. */
  setSolutionPath: (target: string) => void
  t: I18n['t']
}

export interface AnalysisJobState {
  busy: boolean
  /**
   * 분석 작업이 아닌 일(파일 불러오기 등)도 같은 진행 표시를 쓴다.
   * 화면에 "지금 무언가 하는 중"을 나타내는 깃발이 둘이면 서로 어긋난다.
   */
  setBusy: (busy: boolean) => void
  progress: string | null
  jobId: string | null
  /** 분석을 시작한 시각(ms). 없으면 분석 중이 아니다. */
  analysisStartedAt: number | null
  /** 오래 걸리는 중인지. 진행 표시만으로는 알 수 없어서 따로 둔다. */
  longRunning: boolean
  analyze: (targetPath?: string) => Promise<void>
  cancelAnalysis: () => Promise<void>
}

/**
 * 분석 작업 하나의 수명(시작·진행·취소·실패·재접속)을 모은다(T72).
 *
 * 재분석은 언제나 사용자가 시작한다(CLAUDE.md 제약 3). 이 훅은 저장을 감시하지 않고,
 * 이미 서버에서 돌고 있는 작업에만 다시 붙는다.
 */
export function useAnalysisJob(options: UseAnalysisJobOptions): AnalysisJobState {
  const {
    solutionPath, loadGraphData, applyAnalyzedGraph, summarizeFrom, describeError,
    setError, setErrorId, setToast, setTargetPicked, rememberTarget, setSolutionPath, t,
  } = options

  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<string | null>(null)
  const [jobId, setJobId] = useState<string | null>(null)
  const [analysisStartedAt, setAnalysisStartedAt] = useState<number | null>(null)
  const [longRunning, setLongRunning] = useState(false)

  /**
   * 분석을 작업으로 실행해서 단계별 진행 상태를 보여 주고 취소도 받는다.
   * 새 그래프로의 전환은 완료 알림을 받은 뒤 여기서 한 번만 한다.
   */
  const runAnalysisJob = useCallback(async (start: () => Promise<AnalysisJob>, target: string) => {
    setBusy(true)
    setError(null)
    setProgress(null)
    setTargetPicked(false)
    setLongRunning(false)
    try {
      const job = await start()
      setJobId(job.id)
      // 이어 붙은 작업은 시작 시각이 과거다. "오래 걸린다" 안내가 그 시각을 기준으로 뜨게 한다.
      setAnalysisStartedAt(new Date(job.startedAt).getTime())
      let failure: { message: string; code: string | null } | null = null
      let canceled = false
      await watchAnalysisJob(job.id, (event) => {
        if (event.kind === 'progress') setProgress(`${event.stage} · ${event.message}${event.count === null ? '' : ` (${event.count})`}`)
        if (event.kind === 'error') failure = { message: event.message, code: event.code ?? null }
        if (event.kind === 'canceled') canceled = true
      })
      if (canceled) {
        setToast(t('app.canceledNotice'))
        return
      }

      // 서버가 분류한 code로 한 줄 요약을 고르고, 예외 원문은 접어서 보여 준다.
      if (failure !== null) {
        const reason = failure as { message: string; code: string | null }
        setError({
          summary: reason.code ? translateError(t, reason.code, reason.message) : t('app.analyzeFailed'),
          detail: reason.message,
          code: reason.code,
        })
        setErrorId(null)
        return
      }

      const nextGraph = await loadGraphData()
      applyAnalyzedGraph(nextGraph, await summarizeFrom(nextGraph))
      rememberTarget(target)
    } catch (reason) {
      // 작업 API 자체가 없는 환경(구버전 서버)에서만 호환 경로로 다시 시도한다.
      // 그 밖의 실패(같은 대상이 이미 돌고 있음·잘못된 경로 등)에 전체 분석을 한 번 더 돌리면 안 된다.
      const missingJobApi = reason instanceof ApiError && (reason.status === 404 || reason.status === 405)
      if (!missingJobApi) {
        setError(describeError(reason, reason instanceof Error ? reason.message : t('app.analyzeFailed')))
        return
      }

      try {
        const nextSummary = await analyzeTarget(target)
        const nextGraph = await loadGraphData()
        applyAnalyzedGraph(nextGraph, nextSummary)
      } catch (fallbackReason) {
        setError(describeError(fallbackReason, reason instanceof Error ? reason.message : t('app.analyzeFailed')))
      }
    } finally {
      setBusy(false)
      setProgress(null)
      setJobId(null)
      setAnalysisStartedAt(null)
      setLongRunning(false)
    }
  }, [
    loadGraphData, applyAnalyzedGraph, summarizeFrom, describeError,
    setError, setErrorId, setToast, setTargetPicked, rememberTarget, t,
  ])

  const analyze = useCallback(async (targetPath?: string) => {
    const target = (targetPath ?? solutionPath).trim()
    if (!target) return
    await runAnalysisJob(() => startAnalysisJob(target), target)
  }, [solutionPath, runAnalysisJob])

  const cancelAnalysis = useCallback(async () => {
    if (jobId) await cancelAnalysisJob(jobId)
  }, [jobId])

  /**
   * 분석 중에 새로고침하면 작업은 서버에서 계속 도는데 화면만 그 사실을 잊었다.
   * 서버가 알고 있는 작업 목록에서 아직 도는 것을 찾아 진행 상태에 다시 붙는다(T72).
   * 자동으로 분석을 시작하지는 않는다 — 이미 시작된 작업에 붙을 뿐이다.
   */
  useEffect(() => {
    let canceled = false
    void (async () => {
      try {
        const running = (await listAnalysisJobs()).find((job) => job.state === 'Running')
        if (!running || canceled) return
        setSolutionPath(running.targetPath)
        await runAnalysisJob(async () => running, running.targetPath)
      } catch {
        // 작업 API가 없는 서버이거나 서버가 아직 안 떴다. 조용히 넘어간다(배너는 신선도 틱이 띄운다).
      }
    })()
    return () => { canceled = true }
    // 처음 한 번만 확인한다. 이후의 작업은 이 화면이 직접 시작한 것이다.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 오래 걸리면 "기다리는 것 말고 할 수 있는 일"을 알려 준다.
  useEffect(() => {
    if (analysisStartedAt === null) return
    const timer = window.setTimeout(() => setLongRunning(true), longRunningHintMs)
    return () => window.clearTimeout(timer)
  }, [analysisStartedAt])

  return { busy, setBusy, progress, jobId, analysisStartedAt, longRunning, analyze, cancelAnalysis }
}
