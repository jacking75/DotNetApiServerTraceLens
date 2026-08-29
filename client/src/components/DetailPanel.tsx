import { lazy, Suspense, useMemo } from 'react'
import { classMembers, memberKey, memberSign, shortenNamespace, stereotypeOf } from '../graph/classDiagram'
import { relationLabels, type ClassRelation, type RelationType } from '../graph/relations'
import type {
  EntryPoint,
  GraphClass,
  GraphClassMember,
  GraphDocument,
  GraphNamespaceSummary,
  Selection,
} from '../types/graph'
import type { SourceTarget } from './CodePanel'
import { kindColors } from './kindColors'

const SourceView = lazy(() => import('./CodePanel').then((module) => ({ default: module.SourceView })))

export interface DetailActions {
  onSelect: (selection: Selection) => void
  /** 이 위치까지의 흐름을 전체 흐름도에서 연다. */
  onShowFlow: (nodeId: string) => void
  onShowInDiagram: (classId: string) => void
  onFocusOnly: (classId: string) => void
  onGoToNamespace: (namespace: string) => void
}

interface DetailPanelProps {
  graph: GraphDocument | null
  selection: Selection | null
  relations: ClassRelation[]
  namespaces: GraphNamespaceSummary[]
  entryPoints: EntryPoint[]
  namespacePrefix: string
  actions: DetailActions
}

export function DetailPanel({
  graph,
  selection,
  relations,
  namespaces,
  entryPoints,
  namespacePrefix,
  actions,
}: DetailPanelProps) {
  if (!graph || !selection) {
    return <div className="detail-body"><div className="panel-message">그래프에서 항목을 고르면 여기에 상세가 뜬다.</div></div>
  }

  return (
    <div className="detail-body">
      {selection.type === 'class' && (
        <ClassDetail
          graph={graph}
          classId={selection.key}
          relations={relations}
          namespacePrefix={namespacePrefix}
          actions={actions}
        />
      )}
      {selection.type === 'method' && (
        <MethodDetail graph={graph} nodeId={selection.key} entryPoints={entryPoints} actions={actions} />
      )}
      {selection.type === 'namespace' && (
        <NamespaceDetail
          graph={graph}
          namespace={selection.key}
          namespaces={namespaces}
          relations={relations}
          namespacePrefix={namespacePrefix}
          actions={actions}
        />
      )}
    </div>
  )
}

/** G-1 클래스 상세 — 스테레오타입/분류/네임스페이스/멤버/관계 4종/외부 상위 타입. */
function ClassDetail({
  graph,
  classId,
  relations,
  namespacePrefix,
  actions,
}: {
  graph: GraphDocument
  classId: string
  relations: ClassRelation[]
  namespacePrefix: string
  actions: DetailActions
}) {
  const graphClass = graph.classes.find((item) => item.id === classId)
  const nameById = useMemo(() => new Map(graph.classes.map((item) => [item.id, item.name])), [graph.classes])
  if (!graphClass) return <div className="panel-message">이 클래스는 마지막 분석 결과에 없다.</div>

  const { fields, methods } = classMembers(graphClass)
  const stereotype = stereotypeOf(graphClass)
  const outgoing = (type: RelationType) =>
    relations.filter((relation) => relation.type === type && relation.from === classId).map((relation) => relation.to)
  const inbound = [...new Set(relations.filter((relation) => relation.to === classId).map((relation) => relation.from))]

  return (
    <>
      <div className="detail-title">
        <span className="dg-kind" style={{ background: kindColors[graphClass.kind] }} />
        <h3>{stereotype ? `«${stereotype}» ` : ''}{graphClass.name}</h3>
      </div>
      <div className="detail-path">{graphClass.file}:{graphClass.startLine}-{graphClass.endLine}</div>
      <dl className="kv">
        <dt>분류</dt>
        <dd>{graphClass.kind}{graphClass.isAbstract ? ' · abstract' : ''}{graphClass.isStatic ? ' · static' : ''}</dd>
        <dt>네임스페이스</dt>
        <dd>{graphClass.namespace || '(전역)'}</dd>
        <dt>멤버</dt>
        <dd>필드 {fields.length} · 메서드 {methods.length}</dd>
      </dl>

      {(['inherit', 'implement', 'assoc', 'depend'] as RelationType[]).map((type) => {
        const ids = outgoing(type)
        if (ids.length === 0) return null
        return (
          <PillSection
            key={type}
            title={`${relationLabels[type]} (${ids.length})`}
            items={ids.map((id) => ({ label: nameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'class', key: id }) }))}
          />
        )
      })}
      {inbound.length > 0 && (
        <PillSection
          title={`이 클래스를 참조 (${inbound.length})`}
          items={inbound.map((id) => ({ label: nameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'class', key: id }) }))}
        />
      )}
      {graphClass.baseTypeName && !graphClass.baseTypeId && (
        <div className="detail-section">
          <h4>외부 상위 타입</h4>
          <div className="pill-list"><span className="pill">{graphClass.baseTypeName}</span></div>
        </div>
      )}

      {fields.length > 0 && (
        <MemberList title="필드 · 프로퍼티" members={fields} graphClass={graphClass} actions={actions} />
      )}
      <MemberList title="메서드" members={methods} graphClass={graphClass} actions={actions} />

      <div className="detail-section">
        <h4>소스</h4>
        <Suspense fallback={<div className="panel-message">소스를 불러오는 중이다.</div>}>
          <SourceView
            target={{ file: graphClass.file, startLine: graphClass.startLine, endLine: graphClass.endLine }}
            height={220}
            emptyMessage="소스를 불러오는 중이다."
          />
        </Suspense>
      </div>

      <div className="detail-section detail-actions">
        <button type="button" className="btn sm" onClick={() => actions.onFocusOnly(classId)}>이 클래스 1홉만 보기</button>
        <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(classId)}>다이어그램에서 보기</button>
        <button type="button" className="btn sm" onClick={() => actions.onGoToNamespace(graphClass.namespace)}>
          패키지로 이동
        </button>
      </div>
      <div className="detail-hint">네임스페이스 {shortenNamespace(graphClass.namespace, namespacePrefix)}</div>
    </>
  )
}

/** G-2 메서드 상세 — 시그니처·접근제한자·반환타입, 호출/피호출, 소스 미리보기. */
function MethodDetail({
  graph,
  nodeId,
  entryPoints,
  actions,
}: {
  graph: GraphDocument
  nodeId: string
  entryPoints: EntryPoint[]
  actions: DetailActions
}) {
  const node = graph.nodes.find((item) => item.id === nodeId)
  const nodeNameById = useMemo(() => new Map(graph.nodes.map((item) => [item.id, item.displayName])), [graph.nodes])
  const member = useMemo(() => {
    for (const item of graph.classes) {
      const found = item.members.find((candidate) => memberKey(item.id, candidate) === nodeId)
      if (found) return { graphClass: item, member: found }
    }

    return null
  }, [graph.classes, nodeId])

  const graphClass = node ? graph.classes.find((item) => item.id === node.classId) : member?.graphClass
  const entry = entryPoints.find((item) => item.nodeId === nodeId)
  const callees = graph.edges.filter((edge) => edge.from === nodeId).map((edge) => edge.to)
  const callers = graph.edges.filter((edge) => edge.to === nodeId).map((edge) => edge.from)

  // 노드가 없으면(인터페이스 선언 등) 멤버 선언 줄을 대신 보여준다.
  const target: SourceTarget | null = node
    ? { file: node.file, startLine: node.startLine, endLine: node.endLine }
    : member && graphClass
      ? { file: graphClass.file, startLine: member.member.startLine, endLine: member.member.endLine }
      : null
  if (!target) return <div className="panel-message">이 메서드는 마지막 분석 결과에 없다.</div>

  const signature = member?.member.signature ?? node?.displayName ?? nodeId
  const returnType = member?.member.type || node?.returnType || ''

  return (
    <>
      <div className="detail-title">
        <span className="dg-kind" style={{ background: kindColors[graphClass?.kind ?? 'Method'] }} />
        <h3>{node?.displayName ?? `${graphClass?.name ?? ''}.${member?.member.name ?? ''}`}</h3>
      </div>
      <div className="detail-path">{target.file}:{target.startLine}-{target.endLine}</div>
      <dl className="kv">
        <dt>시그니처</dt>
        <dd>
          {member ? `${memberSign(member.member)} ` : ''}{signature}{returnType ? ` : ${returnType}` : ''}
        </dd>
        <dt>접근제한</dt>
        <dd>{member?.member.accessibility ?? node?.accessibility ?? 'Public'}</dd>
        <dt>선언 클래스</dt>
        <dd>{graphClass?.id ?? node?.classId ?? '-'}</dd>
        {entry && <><dt>라우트</dt><dd>{entry.httpMethod} {entry.route}</dd></>}
      </dl>

      <PillSection
        title={`호출하는 대상 (${callees.length})`}
        items={callees.map((id) => ({ label: nodeNameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'method', key: id }) }))}
      />
      <PillSection
        title={`호출하는 쪽 (${callers.length})`}
        items={callers.map((id) => ({ label: nodeNameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'method', key: id }) }))}
      />

      <div className="detail-section">
        <h4>소스</h4>
        <Suspense fallback={<div className="panel-message">소스를 불러오는 중이다.</div>}>
          <SourceView target={target} height={240} emptyMessage="소스를 불러오는 중이다." />
        </Suspense>
      </div>

      <div className="detail-section detail-actions">
        <button type="button" className="btn sm" onClick={() => actions.onShowFlow(nodeId)}>이 위치까지의 흐름 보기</button>
        {graphClass && (
          <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(graphClass.id)}>
            다이어그램에서 보기
          </button>
        )}
      </div>
    </>
  )
}

/** G-3 네임스페이스 상세 — 클래스 목록, 나가고 들어오는 의존. */
function NamespaceDetail({
  graph,
  namespace,
  namespaces,
  relations,
  namespacePrefix,
  actions,
}: {
  graph: GraphDocument
  namespace: string
  namespaces: GraphNamespaceSummary[]
  relations: ClassRelation[]
  namespacePrefix: string
  actions: DetailActions
}) {
  const summary = namespaces.find((item) => item.namespace === namespace)
  const classes = graph.classes.filter((item) => item.namespace === namespace)
  const namespaceOf = useMemo(() => new Map(graph.classes.map((item) => [item.id, item.namespace])), [graph.classes])
  const ids = new Set(classes.map((item) => item.id))
  const outbound = [...new Set(relations
    .filter((relation) => ids.has(relation.from) && !ids.has(relation.to))
    .map((relation) => namespaceOf.get(relation.to) ?? ''))]
  const inbound = [...new Set(relations
    .filter((relation) => !ids.has(relation.from) && ids.has(relation.to))
    .map((relation) => namespaceOf.get(relation.from) ?? ''))]

  return (
    <>
      <div className="detail-title"><h3>{shortenNamespace(namespace, namespacePrefix)}</h3></div>
      <div className="detail-path">{namespace || '(전역 네임스페이스)'}</div>
      <dl className="kv">
        <dt>클래스</dt><dd>{classes.length}개</dd>
        <dt>메서드</dt><dd>{summary?.methodCount ?? classes.reduce((sum, item) => sum + item.methodIds.length, 0)}개</dd>
        <dt>의존</dt><dd>나감 {outbound.length} · 들어옴 {inbound.length}</dd>
      </dl>
      <div className="detail-section">
        <h4>클래스</h4>
        <div className="member-list">
          {classes.map((item) => (
            <button
              type="button"
              className="member"
              key={item.id}
              onClick={() => actions.onSelect({ type: 'class', key: item.id })}
            >
              <span className="dg-kind" style={{ background: kindColors[item.kind] }} />
              <span className="grow">{item.name}</span>
              <span className="line">{item.members.length}</span>
            </button>
          ))}
        </div>
      </div>
      <PillSection
        title={`나가는 의존 (${outbound.length})`}
        items={outbound.map((item) => ({
          label: shortenNamespace(item, namespacePrefix),
          onClick: () => actions.onGoToNamespace(item),
        }))}
      />
      <PillSection
        title={`들어오는 의존 (${inbound.length})`}
        items={inbound.map((item) => ({
          label: shortenNamespace(item, namespacePrefix),
          onClick: () => actions.onGoToNamespace(item),
        }))}
      />
    </>
  )
}

function MemberList({
  title,
  members,
  graphClass,
  actions,
}: {
  title: string
  members: GraphClassMember[]
  graphClass: GraphClass
  actions: DetailActions
}) {
  return (
    <div className="detail-section">
      <h4>{title}</h4>
      <div className="member-list">
        {members.length === 0 && <div className="panel-message tight">멤버가 없다.</div>}
        {members.map((member) => (
          <button
            type="button"
            className="member"
            key={memberKey(graphClass.id, member)}
            onClick={() => actions.onSelect({ type: 'method', key: memberKey(graphClass.id, member) })}
            title={member.nodeId ?? '실행 가능한 노드가 없는 멤버다.'}
          >
            <span className="grow">
              {memberSign(member)} {member.signature ?? member.name}{member.type ? ` : ${member.type}` : ''}
            </span>
            <span className="line">{member.startLine}</span>
          </button>
        ))}
      </div>
    </div>
  )
}

function PillSection({ title, items }: { title: string; items: Array<{ label: string; onClick: () => void }> }) {
  return (
    <div className="detail-section">
      <h4>{title}</h4>
      <div className="pill-list">
        {items.length === 0 && <span className="pill">없음</span>}
        {items.map((item, index) => (
          <button type="button" className="pill" key={`${item.label}-${index}`} onClick={item.onClick}>
            {item.label}
          </button>
        ))}
      </div>
    </div>
  )
}
