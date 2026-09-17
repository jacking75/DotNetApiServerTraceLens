import { useEffect, useState } from 'react'
import { getFreshness } from '../api/client'
import { useI18n } from '../i18n/I18nProvider'
import type { FreshnessResponse } from '../types/graph'
import { HelpLink, type GlossaryTerm } from './HelpDialog'

interface FreshnessBadgeProps {
  canReanalyze: boolean
  refreshKey: number
  analyzing: boolean
  onReanalyze: () => Promise<void>
  /** 신선도가 무엇인지 모르는 사람을 위해 용어집으로 보낸다. */
  onExplain: (term: GlossaryTerm) => void
}

const pollingIntervalMs = 30_000

/**
 * 분석 결과와 현재 소스가 같은지 보여 준다.
 * 이 확인은 파일 목록·해시 비교일 뿐이고 재분석을 실행하지 않는다.
 * 신선도 입력이 없는 옛 그래프는 `fresh`로 단정하지 않고 `unknown`으로 둔다.
 */
export function FreshnessBadge({ canReanalyze, refreshKey, analyzing, onReanalyze, onExplain }: FreshnessBadgeProps) {
  const { t, formatDateTime } = useI18n()
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
    ? t('freshness.fresh')
    : status === 'stale'
      ? freshness?.changedFiles.length
        ? t('freshness.staleCount', { count: freshness.changedFiles.length })
        : t('freshness.stale')
      : status === 'checking'
        ? t('freshness.checking')
        : status === 'idle'
          ? t('freshness.idle')
          : status === 'unknown'
            ? t('freshness.unknown')
            : t('freshness.failed')

  const details = [
    freshness?.changedFiles.length
      ? t('freshness.changedFiles', { files: freshness.changedFiles.slice(0, 8).join(', ') })
      : freshness
        ? t('freshness.lastAnalyzed', { time: formatDateTime(freshness.analyzedAt) })
        : null,
    freshness?.origin === 'offlineSnapshot' ? t('freshness.offline') : null,
    status === 'unknown' ? t('freshness.unknownHint') : null,
    ...(freshness?.reasons ?? []),
  ].filter(Boolean).join('\n')

  return (
    <div className="freshness-control" title={details || undefined}>
      <div className={`freshness-badge ${status}`}>
        <span className="freshness-dot" />
        {label}
      </div>
      <HelpLink term="freshness" onOpen={onExplain} />
      {canReanalyze && (
        <button type="button" onClick={() => void onReanalyze()} disabled={analyzing}>
          {analyzing ? t('freshness.reanalyzing') : t('freshness.reanalyze')}
        </button>
      )}
    </div>
  )
}
