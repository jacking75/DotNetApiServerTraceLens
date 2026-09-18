import { useMemo, useState } from 'react'
import { shortenNamespace } from '../graph/classDiagram'
import { countByLifetime, countSteps } from '../graph/startupFlow'
import type { EntryPoint, GraphClass, GraphNamespaceSummary, Selection, StartupFlowDocument } from '../types/graph'
import { useI18n } from '../i18n/I18nProvider'
import { kindColors } from './kindColors'

interface EntryPointListProps {
  entryPoints: EntryPoint[]
  /** 선택 표시는 엔드포인트 ID로 한다. 같은 메서드에 여러 라우트가 걸릴 수 있기 때문이다. */
  selectedEndpointId: string | null
  emptyMessage: string
  /** 컨트롤러 이름으로도 거를 수 있게 노드 → 클래스 매핑을 받는다. */
  classIdOfMethod?: (nodeId: string) => string | undefined
  /** HTTP 밖 실행 단위 수. 목록이 전부가 아니라는 사실을 목록 안에서 말한다. */
  candidateCount: number
  onShowDiagnostics: () => void
  onSelect: (entryPoint: EntryPoint) => void
}

/** 호출 흐름 보기의 좌측 패널. 진입점이 많은 솔루션을 위해 라우트·동사·컨트롤러로 거른다. */
export function EntryPointList({
  entryPoints,
  selectedEndpointId,
  emptyMessage,
  classIdOfMethod,
  candidateCount,
  onShowDiagnostics,
  onSelect,
}: EntryPointListProps) {
  const { t } = useI18n()
  const [keyword, setKeyword] = useState('')
  const filtered = useMemo(() => {
    const needle = keyword.trim().toLowerCase()
    if (!needle) return entryPoints
    return entryPoints.filter((entryPoint) =>
      entryPoint.route.toLowerCase().includes(needle) ||
      entryPoint.httpMethod.toLowerCase().includes(needle) ||
      (classIdOfMethod?.(entryPoint.nodeId) ?? '').toLowerCase().includes(needle) ||
      entryPoint.handlerDisplayName.toLowerCase().includes(needle))
  }, [entryPoints, keyword, classIdOfMethod])

  return (
    <div className="side-list">
      {entryPoints.length > 0 && (
        <input
          className="side-search"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          placeholder={t('side.entrySearch')}
          aria-label={t('side.entrySearch')}
        />
      )}
      {entryPoints.length === 0 && <div className="panel-message">{emptyMessage}</div>}
      {entryPoints.length > 0 && filtered.length === 0 && (
        <div className="panel-message">{t('side.entryPointsEmpty')}</div>
      )}
      {candidateCount > 0 && (
        <div className="side-footnote">
          {t('side.nonHttpUnits', { count: candidateCount })}
          <button type="button" className="link-button" onClick={onShowDiagnostics}>
            {t('hint.seeDiagnostics')}
          </button>
        </div>
      )}
      {filtered.map((entryPoint) => (
        <button
          type="button"
          key={entryPoint.endpointId}
          className={`side-item${selectedEndpointId === entryPoint.endpointId ? ' selected' : ''}`}
          onClick={() => onSelect(entryPoint)}
          title={[entryPoint.nodeId, ...entryPoint.notes].join(String.fromCharCode(10))}
        >
          <span className={`http ${entryPoint.httpMethod.toLowerCase()}`}>{entryPoint.httpMethod}</span>
          <span className="grow">{entryPoint.route}</span>
          {(entryPoint.apiVersions ?? []).map((version) => (
            <span key={version} className="chip small" title={t('detail.apiVersionTitle')}>
              {t('detail.apiVersion', { version })}
            </span>
          ))}
          {entryPoint.source === 'MinimalApi' && (
            <span className="chip small" title={t('chip.minimalApiTitle')}>{t('chip.minimalApi')}</span>
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
}

interface NamespacePanelProps {
  namespaces: GraphNamespaceSummary[]
  classes: GraphClass[]
  hiddenNamespaces: ReadonlySet<string>
  namespacePrefix: string
  selection: Selection | null
  search: string
  onSearch: (value: string) => void
  onToggle: (namespace: string) => void
  onShowAll: () => void
  onHideAll: () => void
  onGoToNamespace: (namespace: string) => void
  onGoToClass: (graphClass: GraphClass) => void
}

/**
 * 클래스 다이어그램의 좌측 패널. 체크박스는 표시/숨김, 이름 클릭은 그 네임스페이스로 스크롤 + 선택이다.
 * 검색어를 넣으면 이름이 맞는 클래스가 각 네임스페이스 아래에 함께 뜬다.
 */
export function NamespacePanel({
  namespaces,
  classes,
  hiddenNamespaces,
  namespacePrefix,
  selection,
  search,
  onSearch,
  onToggle,
  onShowAll,
  onHideAll,
  onGoToNamespace,
  onGoToClass,
}: NamespacePanelProps) {
  const { t } = useI18n()
  const keyword = search.trim().toLowerCase()
  const classesByNamespace = useMemo(() => {
    const grouped = new Map<string, GraphClass[]>()
    for (const item of classes) {
      const list = grouped.get(item.namespace)
      if (list) list.push(item)
      else grouped.set(item.namespace, [item])
    }

    return grouped
  }, [classes])

  const groups = useMemo(() => {
    const result = new Map<string, GraphNamespaceSummary[]>()
    for (const item of namespaces) {
      const group = item.namespace.split('.').slice(0, 2).join('.')
      const list = result.get(group)
      if (list) list.push(item)
      else result.set(group, [item])
    }

    return [...result.entries()]
  }, [namespaces])

  return (
    <>
      <input
        className="side-search"
        value={search}
        onChange={(event) => onSearch(event.target.value)}
        placeholder={t('side.classSearch')}
        aria-label={t('side.classSearch')}
      />
      <div className="side-tools">
        <button type="button" className="chipbtn" onClick={onShowAll}>{t('side.showAll')}</button>
        <button type="button" className="chipbtn" onClick={onHideAll}>{t('side.hideAll')}</button>
      </div>
      <div className="side-list">
        {namespaces.length === 0 && <div className="panel-message">{t('side.namespacesEmpty')}</div>}
        {groups.map(([group, items]) => {
          const visible = items.filter((item) => !keyword ||
            item.namespace.toLowerCase().includes(keyword) ||
            (classesByNamespace.get(item.namespace) ?? []).some((cls) => cls.name.toLowerCase().includes(keyword)))
          if (visible.length === 0) return null

          return (
            <div key={group}>
              <div className="side-group">{shortenNamespace(group, namespacePrefix)}</div>
              {visible.map((item) => {
                const hidden = hiddenNamespaces.has(item.namespace)
                const selected = selection?.type === 'namespace' && selection.key === item.namespace
                const matched = keyword
                  ? (classesByNamespace.get(item.namespace) ?? []).filter((cls) => cls.name.toLowerCase().includes(keyword))
                  : []

                return (
                  <div key={item.namespace}>
                    <div className={`side-row${selected ? ' selected' : ''}`}>
                      <button
                        type="button"
                        className={`tick${hidden ? '' : ' on'}`}
                        title={t('side.toggleVisibility')}
                        aria-label={t('side.toggleVisibilityOf', { name: item.namespace })}
                        aria-pressed={!hidden}
                        onClick={() => onToggle(item.namespace)}
                      >
                        {hidden ? '' : '✓'}
                      </button>
                      <span className="ns-dot" style={{ background: kindColors[item.kinds[0] ?? 'Method'] }} />
                      <button
                        type="button"
                        className="name"
                        title={item.namespace}
                        onClick={() => onGoToNamespace(item.namespace)}
                      >
                        {shortenNamespace(item.namespace, namespacePrefix)}
                      </button>
                      <span className="count">{item.classCount}</span>
                    </div>
                    {matched.map((graphClass) => (
                      <button
                        type="button"
                        key={graphClass.id}
                        className={`side-item nested${selection?.type === 'class' && selection.key === graphClass.id ? ' selected' : ''}`}
                        onClick={() => onGoToClass(graphClass)}
                        title={graphClass.id}
                      >
                        <span className="ns-dot" style={{ background: kindColors[graphClass.kind] }} />
                        <span className="grow">{graphClass.name}</span>
                      </button>
                    ))}
                  </div>
                )
              })}
            </div>
          )
        })}
      </div>
    </>
  )
}

interface FlowSidePanelProps {
  entryPoints: EntryPoint[]
  classes: GraphClass[]
  classIdOfMethod: (nodeId: string) => string | undefined
  flowEntryId: string | null
  onSelectMap: () => void
  onSelectEntry: (entryPoint: EntryPoint) => void
}

/** 전체 흐름도의 좌측 패널. 맨 위가 전체 지도이고, 그 아래는 컨트롤러별 진입점 목록이다. */
export function FlowSidePanel({
  entryPoints,
  classes,
  classIdOfMethod,
  flowEntryId,
  onSelectMap,
  onSelectEntry,
}: FlowSidePanelProps) {
  const { t } = useI18n()
  const classNameById = useMemo(() => new Map(classes.map((item) => [item.id, item.name])), [classes])
  const byController = useMemo(() => {
    const grouped = new Map<string, EntryPoint[]>()
    for (const entryPoint of entryPoints) {
      const classId = classIdOfMethod(entryPoint.nodeId) ?? t('side.unknownClass')
      const list = grouped.get(classId)
      if (list) list.push(entryPoint)
      else grouped.set(classId, [entryPoint])
    }

    return [...grouped.entries()]
  }, [entryPoints, classIdOfMethod, t])

  return (
    <div className="side-list">
      <button
        type="button"
        className={`side-item${flowEntryId === null ? ' selected' : ''}`}
        onClick={onSelectMap}
      >
        <span className="grow">{t('side.entryMap')}</span>
        <span className="count">{entryPoints.length}</span>
      </button>
      {entryPoints.length === 0 && <div className="panel-message">{t('side.entryPointsEmpty')}</div>}
      {byController.map(([classId, items]) => (
        <div key={classId}>
          <div className="side-group">{classNameById.get(classId) ?? classId}</div>
          {items.map((entryPoint) => (
            <button
              type="button"
              key={`${entryPoint.httpMethod} ${entryPoint.route} ${entryPoint.nodeId}`}
              className={`side-item${flowEntryId === entryPoint.nodeId ? ' selected' : ''}`}
              onClick={() => onSelectEntry(entryPoint)}
              title={entryPoint.nodeId}
            >
              <span className={`http ${entryPoint.httpMethod.toLowerCase()}`}>{entryPoint.httpMethod}</span>
              <span className="grow">{entryPoint.route}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}

/** 시작 흐름의 좌측 패널. Program.cs 위치와 단계 요약만 보여준다(타임라인 자체는 가운데에 있다). */
export function StartupFlowSidePanel({ flow }: { flow: StartupFlowDocument }) {
  const { t } = useI18n()
  const lifetimeCounts = useMemo(() => countByLifetime(flow.serviceSteps), [flow])
  const total = useMemo(() => countSteps(flow), [flow])

  return (
    <div className="side-list">
      <div className="side-group">Program.cs</div>
      <div className="panel-message tight" title={flow.programFile}>{flow.programFile}</div>

      <div className="side-group">{t('side.allSteps')}</div>
      <div className="panel-message tight">{t('side.stepCount', { count: total })}</div>

      <div className="side-group">{t('side.serviceSteps', { count: flow.serviceSteps.length })}</div>
      <div className="panel-message tight">
        Scoped {lifetimeCounts.scoped} · Singleton {lifetimeCounts.singleton} · Transient {lifetimeCounts.transient}
      </div>

      <div className="side-group">{t('side.middlewareSteps', { count: flow.middlewareSteps.length })}</div>
    </div>
  )
}
