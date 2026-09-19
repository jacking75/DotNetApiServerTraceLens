import { useMemo } from 'react'
import { memberKey, shortenNamespace } from '../graph/classDiagram'
import type { EntryGroupBy, EntrySort } from '../graph/entryGroups'
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
import { EntryPointList } from './EntryPointList'
import { kindColors } from './kindColors'

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
  classIdOfMethod: (nodeId: string) => string | undefined
  flowEntryId: string | null
  sort: EntrySort
  onChangeSort: (sort: EntrySort) => void
  groupBy: EntryGroupBy
  onSelectMap: () => void
  onSelectEntry: (entryPoint: EntryPoint) => void
}

/** 전체 흐름도의 좌측 패널. 맨 위가 전체 지도이고, 그 아래는 호출 흐름과 같은 진입점 목록이다. */
export function FlowSidePanel({
  entryPoints,
  classIdOfMethod,
  flowEntryId,
  sort,
  onChangeSort,
  groupBy,
  onSelectMap,
  onSelectEntry,
}: FlowSidePanelProps) {
  const { t } = useI18n()
  // 전체 흐름도는 노드 ID로 고르므로 같은 노드를 가리키는 엔드포인트 중 첫 번째를 선택 표시로 쓴다.
  const selectedEndpointId = flowEntryId
    ? entryPoints.find((item) => item.nodeId === flowEntryId)?.endpointId ?? null
    : null

  return (
    <EntryPointList
      entryPoints={entryPoints}
      selectedEndpointId={selectedEndpointId}
      onSelect={onSelectEntry}
      emptyMessage={t('side.entryPointsEmpty')}
      classIdOfMethod={classIdOfMethod}
      sort={sort}
      onChangeSort={onChangeSort}
      groupBy={groupBy}
      header={(
        <button
          type="button"
          className={`side-item${flowEntryId === null ? ' selected' : ''}`}
          onClick={onSelectMap}
        >
          <span className="grow">{t('side.entryMap')}</span>
          <span className="count">{entryPoints.length}</span>
        </button>
      )}
    />
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

      {/* 좌측과 가운데가 다른 수를 말하면 어느 쪽을 믿어야 할지 알 수 없다. 한 정의만 쓴다(T57). */}
      <div className="side-group">{t('side.allSteps')}</div>
      <div className="panel-message tight">
        {t('startup.topLevelSteps', {
          top: flow.serviceSteps.length + flow.middlewareSteps.length,
          all: total,
        })}
      </div>

      <div className="side-group">{t('side.serviceSteps', { count: flow.serviceSteps.length })}</div>
      <div className="panel-message tight">
        Scoped {lifetimeCounts.scoped} · Singleton {lifetimeCounts.singleton} · Transient {lifetimeCounts.transient}
      </div>

      <div className="side-group">{t('side.middlewareSteps', { count: flow.middlewareSteps.length })}</div>
    </div>
  )
}
