import { useEffect, useMemo, useRef, useState } from 'react'
import {
  collapsedByDefault,
  commonRoutePrefix,
  entrySorts,
  filterEntryPoints,
  findGroupKey,
  groupEntryPoints,
  isGroupOpen,
  openGroup,
  toggleGroup,
  type EntryGroupBy,
  type EntrySort,
} from '../graph/entryGroups'
import type { EntryPoint } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'

export interface EntryPointListProps {
  entryPoints: EntryPoint[]
  /** 선택 표시는 엔드포인트 ID로 한다. 같은 메서드에 여러 라우트가 걸릴 수 있기 때문이다. */
  selectedEndpointId: string | null
  onSelect: (entryPoint: EntryPoint) => void
  emptyMessage: string
  /** 컨트롤러 이름으로도 거를 수 있게 노드 → 클래스 매핑을 받는다. */
  classIdOfMethod?: (nodeId: string) => string | undefined
  showSearch?: boolean
  groupBy?: EntryGroupBy
  sort: EntrySort
  onChangeSort: (sort: EntrySort) => void
  /** HTTP 밖 실행 단위 수. 목록이 전부가 아니라는 사실을 목록 안에서 말한다. */
  candidateCount?: number
  onShowDiagnostics?: () => void
  /** 목록 맨 위에 붙일 줄(전체 흐름도의 "전체 진입점 지도"). */
  header?: React.ReactNode
}

/**
 * 진입점 목록. 호출 흐름·전체 흐름도·DB·캐시가 모두 이 하나를 쓴다.
 * 검색·그룹·접기·선택 표시·정렬이 어디서나 같게 동작해야 "같은 목록"으로 읽힌다.
 */
export function EntryPointList({
  entryPoints,
  selectedEndpointId,
  onSelect,
  emptyMessage,
  classIdOfMethod,
  showSearch = true,
  groupBy = 'controller',
  sort,
  onChangeSort,
  candidateCount = 0,
  onShowDiagnostics,
  header,
}: EntryPointListProps) {
  const { t } = useI18n()
  const [keyword, setKeyword] = useState('')
  const [showHidden, setShowHidden] = useState(false)
  /** 기본 접힘 상태를 뒤집은 그룹. 기본이 펼침이면 여기 든 것이 접힌 것이다. */
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const listRef = useRef<HTMLDivElement>(null)

  const hiddenCount = useMemo(
    () => entryPoints.filter((item) => item.hiddenFromApiExplorer).length,
    [entryPoints])

  const filtered = useMemo(
    () => filterEntryPoints(entryPoints, keyword, showHidden, classIdOfMethod),
    [entryPoints, keyword, showHidden, classIdOfMethod])

  const groups = useMemo(
    () => groupEntryPoints(filtered, {
      groupBy,
      sort,
      classIdOfMethod,
      unknownLabel: t('side.unknownClass'),
    }),
    [filtered, groupBy, sort, classIdOfMethod, t])

  const collapsed = collapsedByDefault(groups.length, keyword.trim().length > 0)
  const selectedGroupKey = useMemo(
    () => findGroupKey(groups, selectedEndpointId),
    [groups, selectedEndpointId])

  // 어떤 방법으로 진입점을 골라도(통합 찾기·URL·영향 목록) 좌측 목록에서 강조가 보여야 한다.
  useEffect(() => {
    if (!selectedGroupKey) return
    setExpanded((current) => openGroup(selectedGroupKey, current, collapsed))
  }, [selectedGroupKey, collapsed])

  /** 목록 안에서 ↑/↓/Home/End로 옮긴다. 진입점이 200개여도 손을 마우스로 옮기지 않게 한다. */
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End']
    if (!keys.includes(event.key)) return
    const items = [...(listRef.current?.querySelectorAll<HTMLButtonElement>('.side-item') ?? [])]
    if (items.length === 0) return
    const index = items.indexOf(document.activeElement as HTMLButtonElement)
    const next = event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? items.length - 1
        : event.key === 'ArrowDown'
          ? Math.min(items.length - 1, index + 1)
          : Math.max(0, index - 1)
    event.preventDefault()
    items[next]?.focus()
  }

  return (
    <div className="side-list" ref={listRef} onKeyDown={onKeyDown}>
      {header}
      {entryPoints.length > 0 && showSearch && (
        <input
          className="side-search"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          placeholder={t('side.entrySearch')}
          aria-label={t('side.entrySearch')}
        />
      )}
      {entryPoints.length > 1 && (
        <div className="side-tools entry-sort" role="group" aria-label={t('side.sortBy')}>
          <span className="hint">{t('side.sortBy')}</span>
          {entrySorts.map((option) => (
            <button
              key={option}
              type="button"
              className={sort === option ? 'chipbtn active' : 'chipbtn'}
              aria-pressed={sort === option}
              onClick={() => onChangeSort(option)}
            >
              {t(`side.sort.${option}` as const)}
            </button>
          ))}
        </div>
      )}
      {hiddenCount > 0 && (
        <button
          type="button"
          className="link-button side-footnote"
          aria-pressed={showHidden}
          onClick={() => setShowHidden((value) => !value)}
        >
          {showHidden ? t('side.hideIgnoredApi') : t('side.showIgnoredApi', { count: hiddenCount })}
        </button>
      )}
      {entryPoints.length === 0 && <div className="panel-message">{emptyMessage}</div>}
      {entryPoints.length > 0 && filtered.length === 0 && (
        <div className="panel-message">{t('side.entryPointsEmpty')}</div>
      )}
      {candidateCount > 0 && onShowDiagnostics && (
        <div className="side-footnote">
          {t('side.nonHttpUnits', { count: candidateCount })}
          <button type="button" className="link-button" onClick={onShowDiagnostics}>
            {t('hint.seeDiagnostics')}
          </button>
        </div>
      )}
      {groups.map((group, index) => {
        const open = isGroupOpen(group.key, expanded, collapsed)
        const prefix = commonRoutePrefix(group.items.map((item) => item.route))
        // 프로젝트 구획은 값이 바뀔 때만 머리글을 그린다(그룹은 이미 구획 순으로 정렬돼 있다).
        const newSection = group.section !== null && group.section !== groups[index - 1]?.section
        return (
          <div key={group.key}>
            {newSection && <div className="side-section">{group.section}</div>}
            <button
              type="button"
              className="side-group toggle"
              aria-expanded={open}
              onClick={() => setExpanded((current) => toggleGroup(group.key, current))}
            >
              <span className="grow" title={group.label}>
                {group.label}
                {prefix && <span className="sub">{prefix}</span>}
              </span>
              {group.source !== 'Controller' && (
                <span className="chip small" title={t('chip.minimalApiTitle')}>{t('chip.minimalApi')}</span>
              )}
              <span className="count">{group.items.length}</span>
            </button>
            {open && group.items.map((entryPoint) => (
              <button
                type="button"
                key={entryPoint.endpointId}
                className={`side-item${selectedEndpointId === entryPoint.endpointId ? ' selected' : ''}`}
                onClick={() => onSelect(entryPoint)}
                title={[entryPoint.nodeId, ...entryPoint.notes].join(String.fromCharCode(10))}
              >
                <span className={`http ${entryPoint.httpMethod.toLowerCase()}`}>{entryPoint.httpMethod}</span>
                <span className="grow">{prefix ? entryPoint.route.slice(prefix.length) || '/' : entryPoint.route}</span>
                {(entryPoint.apiVersions ?? []).map((version) => (
                  <span key={version} className="chip small" title={t('detail.apiVersionTitle')}>
                    {t('detail.apiVersion', { version })}
                  </span>
                ))}
                {entryPoint.authorization?.allowAnonymous && (
                  <span className="chip small" title={t('detail.anonymousTitle')}>{t('detail.anonymous')}</span>
                )}
                {entryPoint.authorization && !entryPoint.authorization.allowAnonymous && (
                  <span
                    className="chip small"
                    title={[
                      t('detail.authorizeTitle'),
                      ...entryPoint.authorization.policies,
                      ...entryPoint.authorization.roles,
                    ].filter(Boolean).join(' · ')}
                  >
                    {t('detail.authorize')}
                  </span>
                )}
                {entryPoint.hiddenFromApiExplorer && (
                  <span className="chip small" title={t('detail.ignoreApiTitle')}>{t('detail.ignoreApi')}</span>
                )}
                {entryPoint.confidence === 'Estimated' && (
                  <span
                    className="chip small warn"
                    title={[t('chip.estimatedTitle'), ...entryPoint.notes].join(String.fromCharCode(10))}
                  >
                    {t('chip.estimated')}
                  </span>
                )}
              </button>
            ))}
          </div>
        )
      })}
    </div>
  )
}
