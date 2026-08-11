import { useEffect, useState } from 'react'
import { getFreshness } from '../api/client'
import type { FreshnessResponse } from '../types/graph'

interface FreshnessBadgeProps {
  canReanalyze: boolean
  refreshKey: number
  analyzing: boolean
  onReanalyze: () => Promise<void>
}

const pollingIntervalMs = 30_000

export function FreshnessBadge({ canReanalyze, refreshKey, analyzing, onReanalyze }: FreshnessBadgeProps) {
  const [freshness, setFreshness] = useState<FreshnessResponse | null>(null)
  const [unavailable, setUnavailable] = useState(false)

  useEffect(() => {
    let active = true
    let polling = false
    const poll = async () => {
      if (polling) return
      polling = true
      try {
        const nextFreshness = await getFreshness()
        if (active) {
          setFreshness(nextFreshness)
          setUnavailable(false)
        }
      } catch {
        if (active) setUnavailable(true)
      } finally {
        polling = false
      }
    }

    void poll()
    const interval = window.setInterval(() => void poll(), pollingIntervalMs)
    return () => {
      active = false
      window.clearInterval(interval)
    }
  }, [refreshKey])

  const status = unavailable ? (freshness ? 'unknown' : 'idle') : freshness?.status ?? 'checking'
  const label = status === 'fresh'
    ? '최신 상태'
    : status === 'stale'
      ? `변경됨${freshness?.changedFiles.length ? ` ${freshness.changedFiles.length}` : ''}`
      : status === 'checking'
        ? '확인 중…'
        : status === 'idle'
          ? '분석 전'
          : '확인 실패'
  const details = freshness?.changedFiles.length
    ? `변경된 파일: ${freshness.changedFiles.join(', ')}`
    : freshness
      ? `마지막 분석: ${new Date(freshness.analyzedAt).toLocaleString('ko-KR')}`
      : undefined

  return (
    <div className="freshness-control" title={details}>
      <div className={`freshness-badge ${status}`}>
        <span className="freshness-dot" />
        {label}
      </div>
      {canReanalyze && (
        <button type="button" onClick={() => void onReanalyze()} disabled={analyzing}>
          {analyzing ? '분석 중…' : '다시 분석'}
        </button>
      )}
    </div>
  )
}
