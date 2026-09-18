import { lazy, Suspense, useMemo } from 'react'
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
import type { SourceTarget } from './CodePanel'
import { confidenceLabel } from '../i18n/labels'
import { HelpLink, type GlossaryTerm } from './HelpDialog'
import { kindColors } from './kindColors'

const SourceView = lazy(() => import('./CodePanel').then((module) => ({ default: module.SourceView })))

export interface DetailActions {
  onSelect: (selection: Selection) => void
  /** 이 위치까지의 흐름을 전체 흐름도에서 연다. */
  onShowFlow: (nodeId: string) => void
  onShowInDiagram: (classId: string) => void
  onFocusOnly: (classId: string) => void
  onGoToNamespace: (namespace: string) => void
  /** 용어집으로 보내는 `?` 링크. */
  onExplain: (term: GlossaryTerm) => void
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
      <div className="detail-path">{step.file}:{step.line}</div>
      <dl className="kv">
        <dt>{t('detail.kind')}</dt>
        <dd>{startupKindLabels[step.kind] ?? step.kind}</dd>
        {step.lifetime && <><dt>{t('detail.lifetime')}</dt><dd>{step.lifetime}</dd></>}
      </dl>

      <div className="detail-section">
        <h4>{t('detail.source')}</h4>
        <Suspense fallback={<div className="panel-message">{t('detail.loadingSource')}</div>}>
          <SourceView
            target={{ file: step.file, startLine: step.line, endLine: step.line }}
            height={160}
            emptyMessage={t('detail.loadingSource')}
          />
        </Suspense>
      </div>

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
      <div className="detail-path">{graphClass.file}:{graphClass.startLine}-{graphClass.endLine}</div>
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

      <div className="detail-section">
        <h4>{t('detail.source')}</h4>
        <Suspense fallback={<div className="panel-message">{t('detail.loadingSource')}</div>}>
          <SourceView
            target={{ file: graphClass.file, startLine: graphClass.startLine, endLine: graphClass.endLine }}
            height={220}
            emptyMessage={t('detail.loadingSource')}
          />
        </Suspense>
      </div>

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
  actions,
}: {
  graph: GraphDocument
  nodeId: string
  entryPoints: EntryPoint[]
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
  const callees = graph.edges.filter((edge) => edge.from === nodeId).map((edge) => edge.to)
  const callers = graph.edges.filter((edge) => edge.to === nodeId).map((edge) => edge.from)

  // 이 메서드에 도달하는 HTTP 진입점이 하나라도 있는지. 없으면 호출 흐름에 그릴 자리가 없다.
  // "호출자가 없다 → 삭제해도 안전"이 아니다. 배치·컨슈머처럼 다른 경로로 실행될 수 있다(T29의 후보 집계).
  const reachedByEntryPoint = useMemo(() => {
    if (!node) return true
    const entryIds = new Set(entryPoints.map((item) => item.nodeId))
    if (entryIds.has(nodeId)) return true
    const incoming = new Map<string, string[]>()
    for (const edge of graph.edges) {
      const list = incoming.get(edge.to)
      if (list) list.push(edge.from)
      else incoming.set(edge.to, [edge.from])
    }

    const seen = new Set([nodeId])
    const queue = [nodeId]
    while (queue.length > 0) {
      const current = queue.shift()!
      if (entryIds.has(current)) return true
      for (const from of incoming.get(current) ?? []) {
        if (seen.has(from)) continue
        seen.add(from)
        queue.push(from)
      }
    }

    return false
  }, [graph.edges, entryPoints, nodeId, node])

  // 호출 근거: 이 노드가 부르거나 이 노드를 부르는 확정 엣지의 실제 호출 줄.
  const evidence = graph.callSites.filter(
    (site) => site.to.length > 0 && (site.from === nodeId || site.to === nodeId))
  // 경계: 확정하지 못해 엣지를 만들지 않은 호출. 목록이 비었다고 "호출 없음"이 아니다.
  const boundaries = graph.callSites.filter((site) => site.to.length === 0 && site.from === nodeId)

  // 노드가 없으면(인터페이스 선언 등) 멤버 선언 줄을 대신 보여준다.
  const target: SourceTarget | null = node
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
      <div className="detail-path">{target.file}:{target.startLine}-{target.endLine}</div>
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
      />

      <CallEvidenceSection
        evidence={evidence}
        boundaries={boundaries}
        hasCallSites={graph.callSites.length > 0}
        nodeId={nodeId}
        nodeNameById={nodeNameById}
        onExplain={actions.onExplain}
      />

      <div className="detail-section">
        <h4>{t('detail.source')}</h4>
        <Suspense fallback={<div className="panel-message">{t('detail.loadingSource')}</div>}>
          <SourceView target={target} height={240} emptyMessage={t('detail.loadingSource')} />
        </Suspense>
      </div>

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
}: {
  evidence: CallSite[]
  boundaries: CallSite[]
  hasCallSites: boolean
  nodeId: string
  nodeNameById: Map<string, string>
  onExplain: (term: GlossaryTerm) => void
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
      <ul className="evidence-list">
        {evidence.map((site, index) => (
          <li key={`${site.from}-${site.to}-${site.file}-${site.line}-${index}`}>
            <span className="evidence-dir">{site.from === nodeId ? '→' : '←'}</span>
            <b>{nodeNameById.get(site.from === nodeId ? site.to : site.from) ?? (site.from === nodeId ? site.to : site.from)}</b>
            <code>{site.file}:{site.line}</code>
            <span className={`chip small${site.confidence === 'Estimated' ? ' warn' : ''}`}>
              {t(resolutionKeys[site.resolution])}
            </span>
            {site.viaLambda && <span className="chip small warn">{t('detail.viaLambda')}</span>}
            {site.note && <div className="detail-hint">{site.note}</div>}
          </li>
        ))}
      </ul>

      <h4>{t('detail.boundaryCalls', { count: boundaries.length })}<HelpLink term="boundary" onOpen={onExplain} /></h4>
      <div className="detail-hint">{t('detail.boundaryHintFull')}</div>
      <ul className="evidence-list">
        {boundaries.map((site, index) => (
          <li key={`${site.file}-${site.line}-${index}`}>
            <b>{site.declaredTarget}</b>
            <code>{site.file}:{site.line}</code>
            <span className="chip small warn">{t(resolutionKeys[site.resolution])}</span>
            {site.viaLambda && <span className="chip small warn">{t('detail.viaLambda')}</span>}
            {site.note && <div className="detail-hint">{site.note}</div>}
          </li>
        ))}
      </ul>
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

function PillSection({ title, items }: { title: string; items: Array<{ label: string; onClick: () => void }> }) {
  const { t } = useI18n()
  return (
    <div className="detail-section">
      <h4>{title}</h4>
      <div className="pill-list">
        {items.length === 0 && <span className="pill">{t('detail.none')}</span>}
        {items.map((item, index) => (
          <button type="button" className="pill" key={`${item.label}-${index}`} onClick={item.onClick}>
            {item.label}
          </button>
        ))}
      </div>
    </div>
  )
}
