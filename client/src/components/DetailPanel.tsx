import { useMemo, useState } from 'react'
import { classMembers, memberKey, memberSign, shortenNamespace, stereotypeOf } from '../graph/classDiagram'
import { relationLabels, type ClassRelation, type RelationType } from '../graph/relations'
import { findStartupStep } from '../graph/startupFlow'
import { useI18n } from '../i18n/I18nProvider'
import type { TranslationKey } from '../i18n/ko'
import type {
  CallResolution,
  CallSite,
  EntryPoint,
  GraphClass,
  GraphClassMember,
  GraphDocument,
  GraphNamespaceSummary,
  Selection,
} from '../types/graph'
import { confidenceLabel } from '../i18n/labels'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
import { SourceLink, type SourceLocation } from './SourceLink'
import { kindColors } from './kindColors'

/** 한 번에 그리는 목록 상한. 넘치면 "더 보기"로 늘린다(3000개짜리 pill 목록을 한 번에 그리지 않는다). */
const listPageSize = 50

export interface DetailActions {
  onSelect: (selection: Selection) => void
  /** 이 위치까지의 흐름을 전체 흐름도에서 연다. */
  onShowFlow: (nodeId: string) => void
  onShowInDiagram: (classId: string) => void
  onFocusOnly: (classId: string) => void
  onGoToNamespace: (namespace: string) => void
  /** 용어집으로 보내는 `?` 링크. */
  onExplain: (term: GlossaryTerm) => void
  /** `파일:줄`을 누르면 코드 보기를 그 위치로 옮긴다. */
  onOpenSource: (location: SourceLocation) => void
  /** Shift+클릭·컨텍스트 메뉴로 에디터에서 연다. */
  onOpenInEditor: (location: SourceLocation) => void
  /** 이 메서드를 부르는 쪽으로 자라는 창을 호출 흐름에 연다. */
  onShowCallerWindow: (nodeId: string) => void
}

interface DetailPanelProps {
  graph: GraphDocument | null
  selection: Selection | null
  relations: ClassRelation[]
  namespaces: GraphNamespaceSummary[]
  entryPoints: EntryPoint[]
  namespacePrefix: string
  /** App이 한 번만 만든 인접 색인. 선택할 때마다 전체 엣지를 다시 훑지 않게 받는다. */
  incomingByNode: Map<string, string[]>
  outgoingByNode: Map<string, string[]>
  actions: DetailActions
}

export function DetailPanel({
  graph,
  selection,
  relations,
  namespaces,
  entryPoints,
  namespacePrefix,
  incomingByNode,
  outgoingByNode,
  actions,
}: DetailPanelProps) {
  const { t } = useI18n()
  if (!graph || !selection) {
    return <div className="detail-body"><div className="panel-message">{t('detail.pickSomething')}</div></div>
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
        <MethodDetail
          graph={graph}
          nodeId={selection.key}
          entryPoints={entryPoints}
          incomingByNode={incomingByNode}
          outgoingByNode={outgoingByNode}
          actions={actions}
        />
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
      {selection.type === 'startupStep' && (
        <StartupStepDetail graph={graph} stepKey={selection.key} actions={actions} />
      )}
    </div>
  )
}

const startupKindLabels: Record<string, string> = {
  Registration: 'detail.startup.Registration',
  Configuration: 'detail.startup.Configuration',
  ExtensionCall: 'detail.startup.ExtensionCall',
  Middleware: 'detail.startup.Middleware',
}

/** 시작 흐름 한 단계의 상세. 등록이면 서비스/구현체 타입으로 바로 이동할 수 있다. */
function StartupStepDetail({
  graph,
  stepKey,
  actions,
}: {
  graph: GraphDocument
  stepKey: string
  actions: DetailActions
}) {
  const { t } = useI18n()
  const step = findStartupStep(graph, stepKey)
  if (!step) return <div className="panel-message">{t('detail.stepMissing')}</div>

  return (
    <>
      <div className="detail-title"><h3>{step.label}</h3></div>
      <div className="detail-path">
        <SourceLink
          file={step.file}
          line={step.line}
          onOpen={actions.onOpenSource}
          onOpenInEditor={actions.onOpenInEditor}
        />
      </div>
      <dl className="kv">
        <dt>{t('detail.kind')}</dt>
        <dd>{startupKindLabels[step.kind] ?? step.kind}</dd>
        {step.lifetime && <><dt>{t('detail.lifetime')}</dt><dd>{step.lifetime}</dd></>}
      </dl>

      {(step.serviceTypeId || step.implementationTypeId) && (
        <div className="detail-section detail-actions">
          {step.serviceTypeId && (
            <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(step.serviceTypeId!)}>
              {t('detail.showClass', { name: step.serviceType ?? step.serviceTypeId })}
            </button>
          )}
          {step.implementationTypeId && (
            <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(step.implementationTypeId!)}>
              {t('detail.showClass', { name: step.implementationType ?? step.implementationTypeId })}
            </button>
          )}
        </div>
      )}
    </>
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
  const { t } = useI18n()
  const graphClass = graph.classes.find((item) => item.id === classId)
  const nameById = useMemo(() => new Map(graph.classes.map((item) => [item.id, item.name])), [graph.classes])
  if (!graphClass) return <div className="panel-message">{t('detail.classMissing')}</div>

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
      <div className="detail-path">
        <SourceLink
          file={graphClass.file}
          line={graphClass.startLine}
          endLine={graphClass.endLine}
          onOpen={actions.onOpenSource}
          onOpenInEditor={actions.onOpenInEditor}
        />
      </div>
      <dl className="kv">
        <dt>{t('detail.category')}</dt>
        <dd>{graphClass.kind}{graphClass.isAbstract ? ' · abstract' : ''}{graphClass.isStatic ? ' · static' : ''}</dd>
        <dt>{t('detail.namespace')}</dt>
        <dd>{graphClass.namespace || t('detail.globalNamespace')}</dd>
        <dt>{t('detail.members')}</dt>
        <dd>{t('detail.memberCounts', { fields: fields.length, methods: methods.length })}</dd>
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
          title={t('detail.referencedBy', { count: inbound.length })}
          items={inbound.map((id) => ({ label: nameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'class', key: id }) }))}
        />
      )}
      {graphClass.baseTypeName && !graphClass.baseTypeId && (
        <div className="detail-section">
          <h4>{t('detail.externalBaseTypes')}</h4>
          <div className="pill-list"><span className="pill">{graphClass.baseTypeName}</span></div>
        </div>
      )}

      {fields.length > 0 && (
        <MemberList title={t('detail.fieldsAndProperties')} members={fields} graphClass={graphClass} actions={actions} />
      )}
      <MemberList title={t('detail.methods')} members={methods} graphClass={graphClass} actions={actions} />

      <div className="detail-section detail-actions">
        <button type="button" className="btn sm" onClick={() => actions.onFocusOnly(classId)}>{t('detail.focusOnly')}</button>
        <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(classId)}>{t('detail.showInDiagram')}</button>
        <button type="button" className="btn sm" onClick={() => actions.onGoToNamespace(graphClass.namespace)}>
          {t('detail.goToPackage')}
        </button>
      </div>
      <div className="detail-hint">{t('detail.namespaceOf', { name: shortenNamespace(graphClass.namespace, namespacePrefix) })}</div>
    </>
  )
}

/** G-2 메서드 상세 — 시그니처·접근제한자·반환타입, 호출/피호출, 소스 미리보기. */
function MethodDetail({
  graph,
  nodeId,
  entryPoints,
  incomingByNode,
  outgoingByNode,
  actions,
}: {
  graph: GraphDocument
  nodeId: string
  entryPoints: EntryPoint[]
  incomingByNode: Map<string, string[]>
  outgoingByNode: Map<string, string[]>
  actions: DetailActions
}) {
  const { t } = useI18n()
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
  /** 이 노드를 핸들러로 가리키는 진입점 수. 기반 클래스 액션이면 파생 수만큼 나온다. */
  const sharedByDerived = entryPoints.filter((item) => item.nodeId === nodeId).length
  const callees = outgoingByNode.get(nodeId) ?? []
  const callers = incomingByNode.get(nodeId) ?? []

  // 이 메서드에 도달하는 HTTP 진입점이 하나라도 있는지. 없으면 호출 흐름에 그릴 자리가 없다.
  // "호출자가 없다 → 삭제해도 안전"이 아니다. 배치·컨슈머처럼 다른 경로로 실행될 수 있다(T29의 후보 집계).
  const reachedByEntryPoint = useMemo(() => {
    if (!node) return true
    const entryIds = new Set(entryPoints.map((item) => item.nodeId))
    if (entryIds.has(nodeId)) return true
    const seen = new Set([nodeId])
    const queue = [nodeId]
    while (queue.length > 0) {
      const current = queue.shift()!
      if (entryIds.has(current)) return true
      for (const from of incomingByNode.get(current) ?? []) {
        if (seen.has(from)) continue
        seen.add(from)
        queue.push(from)
      }
    }

    return false
  }, [incomingByNode, entryPoints, nodeId, node])

  /**
   * 호출 근거: 이 노드가 부르거나 이 노드를 부르는 확정 엣지의 실제 호출 줄.
   * 경계: 확정하지 못해 엣지를 만들지 않은 호출. 목록이 비었다고 "호출 없음"이 아니다.
   * 선택할 때마다 callSites를 두 번 훑지 않도록 한 번에 갈라 담는다.
   */
  const { evidence, boundaries } = useMemo(() => {
    const resolved: CallSite[] = []
    const edges: CallSite[] = []
    for (const site of graph.callSites) {
      if (site.to.length === 0) {
        if (site.from === nodeId) edges.push(site)
        continue
      }

      if (site.from === nodeId || site.to === nodeId) resolved.push(site)
    }

    return { evidence: resolved, boundaries: edges }
  }, [graph.callSites, nodeId])

  // 노드가 없으면(인터페이스 선언 등) 멤버 선언 줄을 대신 보여준다.
  const target: { file: string; startLine: number; endLine: number } | null = node
    ? { file: node.file, startLine: node.startLine, endLine: node.endLine }
    : member && graphClass
      ? { file: graphClass.file, startLine: member.member.startLine, endLine: member.member.endLine }
      : null
  if (!target) return <div className="panel-message">{t('detail.methodMissing')}</div>

  const signature = member?.member.signature ?? node?.displayName ?? nodeId
  const returnType = member?.member.type || node?.returnType || ''

  return (
    <>
      <div className="detail-title">
        <span className="dg-kind" style={{ background: kindColors[graphClass?.kind ?? 'Method'] }} />
        <h3>{node?.displayName ?? `${graphClass?.name ?? ''}.${member?.member.name ?? ''}`}</h3>
      </div>
      <div className="detail-path">
        <SourceLink
          file={target.file}
          line={target.startLine}
          endLine={target.endLine}
          onOpen={actions.onOpenSource}
          onOpenInEditor={actions.onOpenInEditor}
        />
      </div>
      <dl className="kv">
        <dt>{t('detail.signature')}</dt>
        <dd>
          {member ? `${memberSign(member.member)} ` : ''}{signature}{returnType ? ` : ${returnType}` : ''}
        </dd>
        <dt>{t('detail.accessibility')}</dt>
        <dd>{member?.member.accessibility ?? node?.accessibility ?? 'Public'}</dd>
        <dt>{t('detail.declaringClass')}</dt>
        <dd>{graphClass?.id ?? node?.classId ?? '-'}</dd>
        {entry && (
          <>
            <dt>{t('detail.routes')}</dt>
            <dd>{entry.httpMethod} {entry.route}</dd>
            <dt>{t('detail.handler')}</dt>
            <dd>{entry.handlerDisplayName}</dd>
            {(entry.apiVersions ?? []).length > 0 && (
              <>
                <dt>{t('detail.apiVersions')}</dt>
                <dd title={t('detail.apiVersionTitle')}>
                  {(entry.apiVersions ?? []).map((version) => (
                    <span key={version} className="chip small">{t('detail.apiVersion', { version })}</span>
                  ))}
                </dd>
              </>
            )}
            {entry.authorization && (
              <>
                <dt>{t('detail.authorize')}</dt>
                <dd>
                  {entry.authorization.allowAnonymous
                    ? <span className="chip small">{t('detail.anonymous')}</span>
                    : (
                      <>
                        {entry.authorization.policies.map((policy) => (
                          <span key={policy} className="chip small">{policy}</span>
                        ))}
                        {entry.authorization.roles.map((role) => (
                          <span key={role} className="chip small">{role}</span>
                        ))}
                        {entry.authorization.policies.length === 0 && entry.authorization.roles.length === 0 && (
                          <span className="chip small">{t('detail.authorize')}</span>
                        )}
                      </>
                    )}
                  {entry.authorization.confidence === 'Estimated' && (
                    <span className="chip small warn">{t('chip.estimated')}</span>
                  )}
                </dd>
              </>
            )}
            {entry.hiddenFromApiExplorer && (
              <>
                <dt>{t('detail.ignoreApi')}</dt>
                <dd title={t('detail.ignoreApiTitle')}>{t('detail.ignoreApiTitle')}</dd>
              </>
            )}
            <dt>{t('detail.confidence')}<HelpLink term="confidence" onOpen={actions.onExplain} /></dt>
            <dd>
              <span className={`chip small${entry.confidence === 'Estimated' ? ' warn' : ''}`}>
                {confidenceLabel(entry.confidence, t)}
              </span>
            </dd>
          </>
        )}
      </dl>
      {entry && entry.notes.length > 0 && (
        <div className="detail-hint"><b>{t('detail.entryPointNotes')}:</b> {entry.notes.join(' ')}</div>
      )}
      {/*
        기반 클래스에 선언한 액션은 파생 컨트롤러마다 진입점이 하나씩 생기지만 노드는 하나다.
        호출 흐름이 N번 똑같이 보이는 이유를 여기서 말한다.
      */}
      {sharedByDerived > 1 && (
        <div className="detail-hint">{t('detail.baseClassAction', { count: sharedByDerived })}</div>
      )}
      {!reachedByEntryPoint && (
        <div className="detail-hint warn">{t('detail.noEntryPointReaches')}</div>
      )}

      <PillSection
        title={t('detail.callees', { count: callees.length })}
        items={callees.map((id) => ({ label: nodeNameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'method', key: id }) }))}
      />
      <PillSection
        title={t('detail.callers', { count: callers.length })}
        items={callers.map((id) => ({ label: nodeNameById.get(id) ?? id, onClick: () => actions.onSelect({ type: 'method', key: id }) }))}
        extra={callers.length > 0 && (
          <button type="button" className="link-button" onClick={() => actions.onShowCallerWindow(nodeId)}>
            {t('detail.showCallerWindow')}
          </button>
        )}
      />

      <CallEvidenceSection
        evidence={evidence}
        boundaries={boundaries}
        hasCallSites={graph.callSites.length > 0}
        nodeId={nodeId}
        nodeNameById={nodeNameById}
        onExplain={actions.onExplain}
        onOpenSource={actions.onOpenSource}
        onOpenInEditor={actions.onOpenInEditor}
      />

      <div className="detail-section detail-actions">
        <button type="button" className="btn sm" onClick={() => actions.onShowFlow(nodeId)}>{t('detail.showFlowHere')}</button>
        {graphClass && (
          <button type="button" className="btn sm" onClick={() => actions.onShowInDiagram(graphClass.id)}>
            {t('detail.showInDiagram')}
          </button>
        )}
      </div>
    </>
  )
}

/**
 * 호출 근거와 경계 목록.
 * 엣지가 "왜" 생겼는지(직접 호출·DI 등록·유일 구현체 추정)와, 확정하지 못해 엣지를 만들지 않은 호출을 함께 보여 준다.
 */
function CallEvidenceSection({
  evidence,
  boundaries,
  hasCallSites,
  nodeId,
  nodeNameById,
  onExplain,
  onOpenSource,
  onOpenInEditor,
}: {
  evidence: CallSite[]
  boundaries: CallSite[]
  hasCallSites: boolean
  nodeId: string
  nodeNameById: Map<string, string>
  onExplain: (term: GlossaryTerm) => void
  onOpenSource: (location: SourceLocation) => void
  onOpenInEditor: (location: SourceLocation) => void
}) {
  const { t } = useI18n()
  const resolutionKeys: Record<CallResolution, TranslationKey> = {
    Direct: 'detail.resolution.direct',
    DiRegistration: 'detail.resolution.diRegistration',
    SingleImplementation: 'detail.resolution.singleImplementation',
    Ambiguous: 'detail.resolution.ambiguous',
    Unresolved: 'detail.resolution.unresolved',
    External: 'detail.resolution.external',
  }

  if (!hasCallSites) {
    return (
      <div className="detail-section">
        <h4>{t('detail.callEvidence', { count: 0 })}</h4>
        <div className="detail-hint">{t('detail.noEvidence')}</div>
      </div>
    )
  }

  return (
    <div className="detail-section">
      <h4>{t('detail.callEvidence', { count: evidence.length })}<HelpLink term="callSite" onOpen={onExplain} /></h4>
      <div className="detail-hint">{t('detail.callEvidenceHint')}</div>
      <EvidenceList
        items={evidence}
        render={(site, index) => (
          <li key={`${site.from}-${site.to}-${site.file}-${site.line}-${index}`}>
            <span className="evidence-dir">{site.from === nodeId ? '→' : '←'}</span>
            <b>{nodeNameById.get(site.from === nodeId ? site.to : site.from) ?? (site.from === nodeId ? site.to : site.from)}</b>
            <SourceLink file={site.file} line={site.line} onOpen={onOpenSource} onOpenInEditor={onOpenInEditor} />
            <span className={`chip small${site.confidence === 'Estimated' ? ' warn' : ''}`}>
              {t(resolutionKeys[site.resolution])}
            </span>
            {site.viaLambda && <span className="chip small warn">{t('detail.viaLambda')}</span>}
            {site.note && <div className="detail-hint">{site.note}</div>}
          </li>
        )}
      />

      <h4>{t('detail.boundaryCalls', { count: boundaries.length })}<HelpLink term="boundary" onOpen={onExplain} /></h4>
      <div className="detail-hint">{t('detail.boundaryHintFull')}</div>
      <EvidenceList
        items={boundaries}
        render={(site, index) => (
          <li key={`${site.file}-${site.line}-${index}`}>
            <b>{site.declaredTarget}</b>
            <SourceLink file={site.file} line={site.line} onOpen={onOpenSource} onOpenInEditor={onOpenInEditor} />
            <span className="chip small warn">{t(resolutionKeys[site.resolution])}</span>
            {site.viaLambda && <span className="chip small warn">{t('detail.viaLambda')}</span>}
            {site.note && <div className="detail-hint">{site.note}</div>}
          </li>
        )}
      />
    </div>
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
  const { t } = useI18n()
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
      <div className="detail-path">{namespace || t('detail.globalNamespaceFull')}</div>
      <dl className="kv">
        <dt>{t('detail.classes')}</dt><dd>{t('detail.count', { count: classes.length })}</dd>
        <dt>{t('detail.methods')}</dt>
        <dd>
          {t('detail.count', {
            count: summary?.methodCount ?? classes.reduce((sum, item) => sum + item.methodIds.length, 0),
          })}
        </dd>
        <dt>{t('detail.dependencies')}</dt>
        <dd>{t('detail.dependencyCounts', { outbound: outbound.length, inbound: inbound.length })}</dd>
      </dl>
      <div className="detail-section">
        <h4>{t('detail.classes')}</h4>
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
        title={t('detail.outbound', { count: outbound.length })}
        items={outbound.map((item) => ({
          label: shortenNamespace(item, namespacePrefix),
          onClick: () => actions.onGoToNamespace(item),
        }))}
      />
      <PillSection
        title={t('detail.inbound', { count: inbound.length })}
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
  const { t } = useI18n()
  return (
    <div className="detail-section">
      <h4>{title}</h4>
      <div className="member-list">
        {members.length === 0 && <div className="panel-message tight">{t('detail.noMembers')}</div>}
        {members.map((member) => (
          <button
            type="button"
            className="member"
            key={memberKey(graphClass.id, member)}
            onClick={() => actions.onSelect({ type: 'method', key: memberKey(graphClass.id, member) })}
            title={member.nodeId ?? t('detail.noExecutableNode')}
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

function PillSection({
  title,
  items,
  extra,
}: {
  title: string
  items: Array<{ label: string; onClick: () => void }>
  /** 목록 머리에 붙일 버튼(호출자 창 보기 등). */
  extra?: React.ReactNode
}) {
  const { t } = useI18n()
  // 호출자가 3000개인 메서드가 실제로 있다. 한 번에 다 그리면 선택만으로 화면이 멈춘다.
  const [shown, setShown] = useState(listPageSize)
  const visible = items.slice(0, shown)

  return (
    <div className="detail-section">
      <h4>{title}{extra}</h4>
      <div className="pill-list">
        {items.length === 0 && <span className="pill">{t('detail.none')}</span>}
        {visible.map((item, index) => (
          <button type="button" className="pill" key={`${item.label}-${index}`} onClick={item.onClick}>
            {item.label}
          </button>
        ))}
        {items.length > shown && (
          <button
            type="button"
            className="link-button"
            onClick={() => setShown((current) => current + listPageSize)}
          >
            {t('detail.showMore', { count: items.length - shown })}
          </button>
        )}
      </div>
    </div>
  )
}

/** 근거·경계 목록도 같은 상한을 쓴다. 한 메서드의 근거가 수백 줄이면 상세가 스크롤 지옥이 된다. */
function EvidenceList<T>({ items, render }: { items: readonly T[]; render: (item: T, index: number) => React.ReactNode }) {
  const { t } = useI18n()
  const [shown, setShown] = useState(listPageSize)

  return (
    <>
      <ul className="evidence-list">
        {items.slice(0, shown).map(render)}
      </ul>
      {items.length > shown && (
        <button
          type="button"
          className="link-button"
          onClick={() => setShown((current) => current + listPageSize)}
        >
          {t('detail.showMore', { count: items.length - shown })}
        </button>
      )}
    </>
  )
}
