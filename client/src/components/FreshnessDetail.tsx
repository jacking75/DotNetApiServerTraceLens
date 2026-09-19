import { useI18n } from '../i18n/I18nProvider'
import type { FreshnessChangeKind, FreshnessResponse } from '../types/graph'
import type { SourceLocation } from './SourceLink'
import { SourceLink } from './SourceLink'

interface FreshnessDetailProps {
  freshness: FreshnessResponse
  analyzing: boolean
  onReanalyze: () => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
  onClose: () => void
}

const kindOrder: readonly FreshnessChangeKind[] = ['Modified', 'Added', 'Deleted', 'ConfigChanged', 'Unreadable']

/**
 * 신선도 배지를 누르면 열리는 상세. 무엇이 바뀌었는지 종류별로 보여 준다.
 * 바뀐 파일 8개를 `title` 툴팁에만 넣어 두면 실제로는 아무도 못 읽는다.
 */
export function FreshnessDetail({
  freshness,
  analyzing,
  onReanalyze,
  onOpenSource,
  onOpenInEditor,
  onClose,
}: FreshnessDetailProps) {
  const { t, formatDateTime } = useI18n()
  const grouped = kindOrder
    .map((kind) => ({ kind, items: freshness.changes.filter((item) => item.kind === kind) }))
    .filter((group) => group.items.length > 0)

  return (
    <div className="freshness-detail" role="dialog" aria-label={t('freshness.detailTitle')}>
      <header>
        <span>{t('freshness.detailTitle')}</span>
        <button type="button" className="link-button" onClick={onClose}>{t('ask.close')}</button>
      </header>
      <div className="hint">{t('freshness.lastAnalyzed', { time: formatDateTime(freshness.analyzedAt) })}</div>
      {grouped.length === 0 && <div className="panel-message tight">{t('freshness.noChanges')}</div>}
      {grouped.map((group) => (
        <div key={group.kind}>
          <div className="side-group">
            {t(`freshness.kind.${group.kind}` as const)} <span className="count">{group.items.length}</span>
          </div>
          <ul className="freshness-files">
            {group.items.slice(0, 50).map((item) => (
              <li key={`${group.kind}-${item.path}`}>
                {group.kind === 'Deleted'
                  ? <code>{item.path}</code>
                  : <SourceLink file={item.path} line={1} onOpen={onOpenSource} onOpenInEditor={onOpenInEditor} />}
              </li>
            ))}
          </ul>
          {group.items.length > 50 && (
            <div className="hint">{t('freshness.moreFiles', { count: group.items.length - 50 })}</div>
          )}
        </div>
      ))}
      {freshness.reasons.length > 0 && (
        <ul className="context-limitations">
          {freshness.reasons.map((reason) => <li key={reason}>{reason}</li>)}
        </ul>
      )}
      <button type="button" className="primary" onClick={onReanalyze} disabled={analyzing}>
        {analyzing ? t('freshness.reanalyzing') : t('freshness.reanalyze')}
      </button>
    </div>
  )
}
