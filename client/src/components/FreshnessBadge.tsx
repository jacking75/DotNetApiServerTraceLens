import { useEffect, useState } from 'react'
import { getFreshness } from '../api/client'
import { useI18n } from '../i18n/I18nProvider'
import type { FreshnessResponse } from '../types/graph'
import { FreshnessDetail } from './FreshnessDetail'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
import type { SourceLocation } from './SourceLink'

interface FreshnessBadgeProps {
  canReanalyze: boolean
  refreshKey: number
  analyzing: boolean
  onReanalyze: () => Promise<void>
  /** 신선도가 무엇인지 모르는 사람을 위해 용어집으로 보낸다. */
  onExplain: (term: GlossaryTerm) => void
  /** 신선도를 확인할 때 서버 연결 여부가 함께 드러난다. 한곳에서 잡아 배너로 알린다. */
  onServerReachable: (reachable: boolean) => void
  /** 지금 신선도를 App에도 알린다. 보기 상단의 stale 배너가 이 값을 쓴다. */
  onFreshness: (freshness: FreshnessResponse | null) => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
}

const pollingIntervalMs = 30_000

/**
 * 분석 결과와 현재 소스가 같은지 보여 준다.
 * 이 확인은 파일 목록·해시 비교일 뿐이고 재분석을 실행하지 않는다.
 * 신선도 입력이 없는 옛 그래프는 `fresh`로 단정하지 않고 `unknown`으로 둔다.
 */
export function FreshnessBadge({
  canReanalyze,
  refreshKey,
  analyzing,
  onReanalyze,
  onExplain,
  onServerReachable,
  onFreshness,
  onOpenSource,
  onOpenInEditor,
}: FreshnessBadgeProps) {
  const { t, formatDateTime } = useI18n()
  const [freshness, setFreshness] = useState<FreshnessResponse | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const [detailOpen, setDetailOpen] = useState(false)

  // 콜백은 매 렌더 새로 올 수 있다. 폴링 타이머를 다시 달지 않도록 상태로 옮겨 알린다.
  useEffect(() => { onFreshness(unavailable ? null : freshness) }, [freshness, unavailable, onFreshness])
  useEffect(() => { onServerReachable(!unavailable) }, [unavailable, onServerReachable])

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
      <button
        type="button"
        className={`freshness-badge ${status}`}
        aria-expanded={detailOpen}
        title={t('freshness.detailTitle')}
        onClick={() => setDetailOpen((value) => !value)}
        disabled={!freshness}
      >
        <span className="freshness-dot" />
        {label}
      </button>
      <HelpLink term="freshness" onOpen={onExplain} />
      {canReanalyze && (
        <button type="button" onClick={() => void onReanalyze()} disabled={analyzing}>
          {analyzing ? t('freshness.reanalyzing') : t('freshness.reanalyze')}
        </button>
      )}
      {detailOpen && freshness && (
        <FreshnessDetail
          freshness={freshness}
          analyzing={analyzing}
          onReanalyze={() => { setDetailOpen(false); void onReanalyze() }}
          onOpenSource={(location) => { setDetailOpen(false); onOpenSource(location) }}
          onOpenInEditor={onOpenInEditor}
          onClose={() => setDetailOpen(false)}
        />
      )}
    </div>
  )
}
