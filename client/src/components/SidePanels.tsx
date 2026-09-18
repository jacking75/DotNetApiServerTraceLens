import { useMemo, useState } from 'react'
import { memberKey, shortenNamespace } from '../graph/classDiagram'
import { countByLifetime, countSteps } from '../graph/startupFlow'
import type {
  EntryPoint,
  GraphClass,
  GraphClassMember,
  GraphNamespaceSummary,
  Selection,
  StartupFlowDocument,
} from '../types/graph'
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

/**
 * 진입점을 컨트롤러(선언 클래스)별로 묶는다. 호출 흐름 목록과 전체 흐름도 사이드가 같은 규칙을 쓴다.
 * 클래스를 알 수 없는 진입점(Minimal API 람다 등)은 한 그룹으로 모은다.
 */
export function groupByController(
  entryPoints: readonly EntryPoint[],
  classIdOfMethod: ((nodeId: string) => string | undefined) | undefined,
  unknownLabel: string,
): [string, EntryPoint[]][] {
  const grouped = new Map<string, EntryPoint[]>()
  for (const entryPoint of entryPoints) {
    const classId = classIdOfMethod?.(entryPoint.nodeId) ?? unknownLabel
    const list = grouped.get(classId)
    if (list) list.push(entryPoint)
    else grouped.set(classId, [entryPoint])
  }

  return [...grouped.entries()]
}

/** 이 개수를 넘는 컨트롤러 그룹은 접힌 채로 시작한다. 200개짜리 목록이 한 화면을 넘지 않게 한다. */
const collapseGroupsAbove = 20

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
  /** 기본 접힘 상태를 뒤집은 그룹. 목록이 짧으면 펼침이 기본이라 여기 든 것이 접힌 것이다. */
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const filtered = useMemo(() => {
    const needle = keyword.trim().toLowerCase()
    if (!needle) return entryPoints
    return entryPoints.filter((entryPoint) =>
      entryPoint.route.toLowerCase().includes(needle) ||
      entryPoint.httpMethod.toLowerCase().includes(needle) ||
      (classIdOfMethod?.(entryPoint.nodeId) ?? '').toLowerCase().includes(needle) ||
      entryPoint.handlerDisplayName.toLowerCase().includes(needle))
  }, [entryPoints, keyword, classIdOfMethod])

  const groups = useMemo(
    () => groupByController(filtered, classIdOfMethod, t('side.unknownClass')),
    [filtered, classIdOfMethod, t])
  // 컨트롤러가 많으면 접힌 채로 시작한다. 검색 중에는 결과가 보여야 하므로 항상 펼친다.
  const collapsedByDefault = groups.length > collapseGroupsAbove && keyword.trim().length === 0

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
      {groups.map(([classId, items]) => {
        const open = expanded.has(classId) !== collapsedByDefault
        return (
          <div key={classId}>
            <button
              type="button"
              className="side-group toggle"
              aria-expanded={open}
              onClick={() => setExpanded((current) => {
                const next = new Set(current)
                if (!next.delete(classId)) next.add(classId)
                return next
              })}
            >
              <span className="grow">{classId.split('.').pop() ?? classId}</span>
              <span className="count">{items.length}</span>
            </button>
            {open && items.map((entryPoint) => (
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
      })}
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
  /** 검색으로 찾은 멤버를 눌렀을 때. 클래스가 아니라 그 멤버를 선택한다. */
  onSelectMember: (graphClass: GraphClass, member: GraphClassMember) => void
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
  onSelectMember,
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
            (classesByNamespace.get(item.namespace) ?? []).some((cls) =>
              cls.name.toLowerCase().includes(keyword) ||
              cls.members.some((member) => member.name.toLowerCase().includes(keyword))))
          if (visible.length === 0) return null

          return (
            <div key={group}>
              <div className="side-group">{shortenNamespace(group, namespacePrefix)}</div>
              {visible.map((item) => {
                const hidden = hiddenNamespaces.has(item.namespace)
                const selected = selection?.type === 'namespace' && selection.key === item.namespace
                // 클래스 이름이 맞으면 클래스만, 멤버 이름이 맞으면 그 멤버를 클래스 아래 들여쓴 줄로 보여 준다.
                const matched = keyword
                  ? (classesByNamespace.get(item.namespace) ?? [])
                    .map((cls) => ({
                      cls,
                      members: cls.name.toLowerCase().includes(keyword)
                        ? []
                        : cls.members.filter((member) => member.name.toLowerCase().includes(keyword)),
                    }))
                    .filter((entry) => entry.cls.name.toLowerCase().includes(keyword) || entry.members.length > 0)
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
                    {matched.map(({ cls: graphClass, members }) => (
                      <div key={graphClass.id}>
                        <button
                          type="button"
                          className={`side-item nested${selection?.type === 'class' && selection.key === graphClass.id ? ' selected' : ''}`}
                          onClick={() => onGoToClass(graphClass)}
                          title={graphClass.id}
                        >
                          <span className="ns-dot" style={{ background: kindColors[graphClass.kind] }} />
                          <span className="grow">{graphClass.name}</span>
                        </button>
                        {members.map((member) => (
                          <button
                            type="button"
                            key={memberKey(graphClass.id, member)}
                            className={`side-item nested deep${
                              selection?.type === 'method' && selection.key === memberKey(graphClass.id, member) ? ' selected' : ''}`}
                            onClick={() => onSelectMember(graphClass, member)}
                            title={member.signature ?? member.name}
                          >
                            <span className="grow">{member.name}</span>
                          </button>
                        ))}
                      </div>
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
  const byController = useMemo(
    () => groupByController(entryPoints, classIdOfMethod, t('side.unknownClass')),
    [entryPoints, classIdOfMethod, t])

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
